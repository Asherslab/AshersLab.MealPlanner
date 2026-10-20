using Grpc.Net.Client.Web;
using ProtoBuf.Grpc.ClientFactory;

namespace AshersLab.MealPlanner.WASM.Extensions;

public static class GrpcServiceExtensions
{
    /// <summary>
    /// Registers a code-first gRPC-Web client pointed at the origin that served the app, under its base path.
    /// </summary>
    /// <remarks>
    /// <b>The address is the app's base URI, never <c>/</c> and never a service-discovery name.</b> The
    /// gRPC host serves this app itself, so the API is always same-origin - and under Home Assistant ingress
    /// the app lives at <c>/api/hassio_ingress/&lt;token&gt;/</c>, where a root-relative call would leave
    /// the add-on entirely (docs/modules/infrastructure/deployment.md). The GSBC repos' <c>https://yarp</c>
    /// does not apply: there is no YARP.
    /// <para>
    /// <b>Setting the address is not enough.</b> <c>GrpcChannel</c> keeps only the scheme, host and port of
    /// its address and sends every call to <c>/{service}/{method}</c> from the root, so without
    /// <see cref="BasePathHandler"/> every call under ingress is a 404 ("Unimplemented") while the page itself
    /// loads fine. At <c>/</c> - local development, the test host - the two are the same, which is why only
    /// a run behind a prefix catches it.
    /// </para>
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
            .ConfigurePrimaryHttpMessageHandler(() =>
                new GrpcWebHandler(new BasePathHandler(baseAddress, new HttpClientHandler())));

        return services;
    }

    /// <summary>Puts the base path back in front of the <c>/{service}/{method}</c> the channel sends.</summary>
    public sealed class BasePathHandler(Uri baseAddress, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly string _basePath = baseAddress.AbsolutePath.TrimEnd('/');

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_basePath.Length > 0 && request.RequestUri is { } uri)
                request.RequestUri = new UriBuilder(uri) { Path = _basePath + uri.AbsolutePath }.Uri;

            return base.SendAsync(request, cancellationToken);
        }
    }
}
