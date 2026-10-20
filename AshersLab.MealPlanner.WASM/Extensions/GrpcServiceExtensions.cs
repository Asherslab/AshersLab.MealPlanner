using Grpc.Net.Client.Web;
using ProtoBuf.Grpc.ClientFactory;

namespace AshersLab.MealPlanner.WASM.Extensions;

public static class GrpcServiceExtensions
{
    /// <summary>
    /// Registers a code-first gRPC-Web client pointed at the origin that served the app.
    /// </summary>
    /// <remarks>
    /// <b>The address is the app's base URI, never <c>/</c> and never a service-discovery name.</b> The
    /// gRPC host serves this app itself, so the API is always same-origin - and under Home Assistant ingress
    /// the app lives at <c>/api/hassio_ingress/&lt;token&gt;/</c>, where a root-relative call would leave
    /// the add-on entirely (docs/work/2026-10-deployment.md). The GSBC repos' <c>https://yarp</c> does not
    /// apply: there is no YARP.
    /// <para>
    /// <see cref="GrpcWebHandler"/> is not optional: a browser cannot speak HTTP/2 gRPC framing.
    /// </para>
    /// </remarks>
    // Named AddCodeFirstClient, not AddGrpcClient: Grpc.Net.ClientFactory already ships an AddGrpcClient<T>,
    // and a same-named extension here is an ambiguous-call error at every call site.
    public static IServiceCollection AddCodeFirstClient<T>(this IServiceCollection services, Uri baseAddress)
        where T : class
    {
        services
            .AddCodeFirstGrpcClient<T>(typeof(T).FullName!, x => { x.Address = baseAddress; })
            .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(new HttpClientHandler()));

        return services;
    }
}
