# cert-manager + AGC HTTP-01 TLS

The AppHost wires this up almost end-to-end:

- `aks.AddHelmChart("cert-manager", ...)` installs cert-manager `v1.18.2` with
  Gateway API support enabled.
- `storefront-gw.WithTls().WithGatewayAnnotation("cert-manager.io/cluster-issuer", "letsencrypt-staging")`
  emits an HTTPS listener on the gateway with no hostname plus the cert-manager
  annotation. After Helm deploys it, Aspire's `tls-fqdn-discovery` pipeline step:

  1. Polls the Gateway's `status.addresses` for the AGC-assigned FQDN
     (`<random>.fz<n>.alb.azure.com`).
  2. JSON-patches the listener `hostname` field with that FQDN.
  3. Creates a self-signed bootstrap TLS Secret so the listener is functional.
  4. Transfers field ownership back to Helm via server-side apply.

  Once cert-manager observes the Gateway listener with TLS configuration and the
  `cert-manager.io/cluster-issuer` annotation, it auto-creates a `Certificate`,
  runs the HTTP-01 challenge against the AGC FQDN, and replaces the bootstrap
  Secret with a real Let's Encrypt certificate.

The only thing the AppHost can't do is create the `ClusterIssuer` itself
(cert-manager doesn't ship with one — they're per-environment).

## One-time post-deploy step

Edit `cluster-issuer.yaml` and replace `REPLACE_ME@example.com` with a real
contact email, then apply:

```bash
kubectl apply -f playground/AksDemo/k8s/cluster-issuer.yaml
```

It defines two ClusterIssuers — `letsencrypt-staging` (the one the AppHost
references by default) and `letsencrypt-prod` (swap the annotation in the
AppHost when you're ready to issue a trusted cert).

## Verifying

After `aspire deploy` finishes the `tls-fqdn-discovery` step:

```bash
# The patched hostname on the gateway listener
kubectl get gateway storefront-gw -o jsonpath='{.spec.listeners[?(@.name=="https")].hostname}'

# cert-manager objects working through the HTTP-01 flow
kubectl get certificate,certificaterequest,order,challenge -A

# Once the certificate is Ready, hit it
HOST=$(kubectl get gateway storefront-gw -o jsonpath='{.status.addresses[0].value}')
curl -v "https://$HOST/api"
```

The very first request will use the LE **staging** issuer, so `curl` will
report an untrusted issuer — pass `--insecure` (or import the staging root)
until you flip the AppHost annotation to `letsencrypt-prod` and redeploy.

## Cleanup

`aspire destroy` uninstalls cert-manager (`WithDestroy()` is set on the helm
chart). The `ClusterIssuer` resources are removed when cert-manager's CRDs are
deleted; the Certificate / Secret / bootstrap Secret follow with the rest of
the cluster when the underlying AKS resource group is destroyed.

## Why this isn't a one-call API yet

The next iteration would be something like:

```csharp
aks.AddCertManager()
   .AddAcmeIssuer("letsencrypt", email: "...", server: AcmeServer.LetsEncryptStaging);

aks.AddGateway("storefront-gw")
   .WithTls(issuer: "letsencrypt");
```

…but we want to validate the underlying Helm + manifest flow on a real cluster
first before locking the resource model down.
