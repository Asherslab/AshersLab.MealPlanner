# The one image: the gRPC host, serving gRPC-Web AND the Blazor WASM app, with SQLite under /data. Runs as a
# Home Assistant add-on (mealplanner/config.yaml) and, later, as a plain container. See
# docs/modules/infrastructure/deployment.md.
#
# Built for linux/amd64 and linux/arm64 WITHOUT emulation. The build stage runs on the builder's own platform
# ($BUILDPLATFORM) and publishes portable (no -r): the output is IL, the WASM files are platform-free, and
# SQLite's native library ships for every RID under runtimes/. So one publish serves both images, and the
# per-platform final stage only COPYs - nothing executes under QEMU. Publishing the relinked WASM runtime under
# QEMU took tens of minutes in the GSBC repos; here it is not done at all.

# ---------- Build stage: always the builder's native platform ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-local
WORKDIR /src

# The WASM project relinks the .NET runtime (WasmBuildNative, for the browser's SQLite), which needs the
# wasm-tools workload - and its Emscripten toolchain needs python3, which the SDK image does not carry. Without
# the workload the restore fails; with a workload from another SDK band the build succeeds and the page dies
# with "function signature mismatch". Installed here, by this SDK, so the two always match.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/* \
    && dotnet workload install wasm-tools

# Every project the host references, so this restore layer is complete. Without them restore prints "Skipping
# project ... because it was not found" and the publish below restores the rest again, uncached.
COPY ["AshersLab.MealPlanner.Grpc/AshersLab.MealPlanner.Grpc.csproj", "AshersLab.MealPlanner.Grpc/"]
COPY ["AshersLab.MealPlanner.WASM/AshersLab.MealPlanner.WASM.csproj", "AshersLab.MealPlanner.WASM/"]
COPY ["AshersLab.MealPlanner.LocalData/AshersLab.MealPlanner.LocalData.csproj", "AshersLab.MealPlanner.LocalData/"]
COPY ["AshersLab.MealPlanner.ServiceDefaults/AshersLab.MealPlanner.ServiceDefaults.csproj", "AshersLab.MealPlanner.ServiceDefaults/"]
COPY ["AshersLab.MealPlanner.Shared.Contracts/AshersLab.MealPlanner.Shared.Contracts.csproj", "AshersLab.MealPlanner.Shared.Contracts/"]
COPY ["AshersLab.MealPlanner.Shared.Data/AshersLab.MealPlanner.Shared.Data.csproj", "AshersLab.MealPlanner.Shared.Data/"]
COPY ["AshersLab.MealPlanner.Shared.Domain/AshersLab.MealPlanner.Shared.Domain.csproj", "AshersLab.MealPlanner.Shared.Domain/"]
RUN dotnet restore "AshersLab.MealPlanner.Grpc/AshersLab.MealPlanner.Grpc.csproj"

COPY . .

# Version is what ServerInfo reports and the home page shows - the semantic version CI computed, so the page
# says which image is running.
RUN dotnet publish "AshersLab.MealPlanner.Grpc/AshersLab.MealPlanner.Grpc.csproj" \
        -c Release -o /app/publish --no-restore \
        -p:UseAppHost=false -p:Version=$VERSION \
    # The image is glibc Debian on x64 or arm64 only; every other RID's native SQLite is dead weight on a Pi's
    # SD card. Both kept, because this one publish feeds both platforms' images.
    && find /app/publish/runtimes -mindepth 1 -maxdepth 1 ! -name linux-x64 ! -name linux-arm64 -exec rm -rf {} + \
    # Fail the build, not the first page load, if the WASM app did not make it into the host's web root.
    && test -f /app/publish/wwwroot/index.html \
    && ls /app/publish/wwwroot/_framework/blazor.webassembly.js

# ---------- Final stage: one per target platform, COPY only ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Root, not the image's default `app` user: the Home Assistant Supervisor mounts /data owned by root and offers
# no way to change that, so as `app` the first migration fails with "unable to open database file". Revisit for
# Kubernetes (fsGroup) in slice 14.
USER root

# 8099 is the add-on's ingress_port. DataDirectory is where the SQLite file lives; /data is the add-on's
# persistent, backed-up volume. Ingress trust is NOT set here - the add-on's config.yaml sets it, so the same
# image run anywhere else does not believe headers from an address that means nothing there.
ENV ASPNETCORE_HTTP_PORTS=8099 \
    DataDirectory=/data
EXPOSE 8099
VOLUME /data

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AshersLab.MealPlanner.Grpc.dll"]
