# Deploy the public pages

The two folders under `sites/` are independent static websites. Plain HTML/CSS keeps the pages deployable without a build framework or application server. No deployment or DNS change has been made by this task.

## Preview locally

From the repository root, run each in its own terminal:

```sh
python -m http.server 4173 --bind 127.0.0.1 --directory sites/brand
python -m http.server 4174 --bind 127.0.0.1 --directory sites/markets
```

Open http://127.0.0.1:4173 and http://127.0.0.1:4174. Cross-site links intentionally use the final domains; use the local addresses to inspect the second page before DNS is active.

## Cloudflare Pages

Create two Pages projects connected to [raadupop/primescore-ai](https://github.com/raadupop/primescore-ai), production branch `master`, no framework preset, build command `exit 0`:

| Proposed Pages project | Build output directory | Custom domain |
| --- | --- | --- |
| `primescore-ai-brand` | `sites/brand` | `primescore.ai` |
| `primescore-ai-markets` | `sites/markets` | `markets.primescore.ai` |

Project names are suggestions, not allocated resources. Use the actual `*.pages.dev` hostname shown after creation. [Cloudflare static HTML setup](https://developers.cloudflare.com/pages/framework-guides/deploy-anything/)

## DNS records

First add each domain in its Pages project's **Custom domains** settings. For the apex, `primescore.ai` must be a zone in the same Cloudflare account and the registrar must use that zone's assigned Cloudflare nameservers. Cloudflare then creates the flattened apex CNAME. A subdomain can also use an external DNS provider. Creating only a CNAME without associating the custom domain in Pages can fail. [Cloudflare custom-domain instructions](https://developers.cloudflare.com/pages/configuration/custom-domains/)

| Type | Name | Target | TTL |
| --- | --- | --- | --- |
| CNAME | `@` | `primescore-ai-brand.pages.dev` **if assigned** | Auto |
| CNAME | `markets` | `primescore-ai-markets.pages.dev` **if assigned** | Auto |

Replace targets with the project's actual assigned hostnames. No A record or fixed IP address is required for this setup. Preserve existing MX/TXT/email records. Wait for Pages domain activation and HTTPS certificates, then verify both pages and their cross-links.

## What is separate

The Python replay dashboard runs locally with `python scripts/demo.py`; Cloudflare Pages does not host that Python process. The pages link to its repository instructions, not to a non-existent hosted demo. Provider histories remain local replay fixtures rather than website download assets.

Contact links use Radu's existing GitHub profile. No email mailbox, contact backend, analytics tracker or account system has been invented. MIT remains a proposal for original code; the website does not claim the repository is licensed open source.
