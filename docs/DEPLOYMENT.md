# Website deployment

## Cloudflare Pages

Connect [raadupop/primescore-ai](https://github.com/raadupop/primescore-ai) to two Pages projects:

| Project name | Build output directory | Domain |
| --- | --- | --- |
| `primescore-ai-brand` | `sites/brand` | `primescore.ai` |
| `primescore-ai-markets` | `sites/markets` | `markets.primescore.ai` |

For both projects, use production branch `master`, framework preset **None**, and build command `exit 0`. Leave **Root directory** blank. Verify each deployment at its assigned `*.pages.dev` address.

## Custom domains

1. Add `primescore.ai` as a zone in the same Cloudflare account and configure its assigned nameservers at the registrar.
2. Register each domain under the corresponding Pages project's **Custom domains** settings. Cloudflare creates the DNS records for domains in that zone.
3. Confirm both domains are active, HTTPS works, and links between the sites resolve.

The resulting records are:

| Type | Name | Target | TTL |
| --- | --- | --- | --- |
| CNAME | `@` | `primescore-ai-brand.pages.dev` | Auto |
| CNAME | `markets` | `primescore-ai-markets.pages.dev` | Auto |

Use the assigned Pages hostnames if the project names differ. Cloudflare flattens the apex CNAME; no A record is required. Preserve existing email records when configuring the zone.

## Local preview

Run each command in a separate terminal from the repository root:

```sh
python -m http.server 4173 --bind 127.0.0.1 --directory sites/brand
python -m http.server 4174 --bind 127.0.0.1 --directory sites/markets
```

Open [brand](http://127.0.0.1:4173) or [markets](http://127.0.0.1:4174). The Python dashboard has its own [run instructions](DEMO.md).

Reference: Cloudflare [static HTML setup](https://developers.cloudflare.com/pages/framework-guides/deploy-anything/) and [custom domains](https://developers.cloudflare.com/pages/configuration/custom-domains/).
