#!/bin/sh
# Generates the DEV vendor keypair (private key stays out of git) and the signed entitlement files
# both SKUs use locally (ADR-0016, ADR-0025). Production keys are vendor-held; this never runs in
# production, and eng/dev-keys/ is gitignored precisely so a dev key cannot become a shipped one.
set -eu
cd "$(dirname "$0")/.."

mkdir -p eng/dev-keys deploy/entitlements

[ -f eng/dev-keys/vendor-private.pem ] || \
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out eng/dev-keys/vendor-private.pem 2>/dev/null

# One vendor, two products: the same public key is baked into both hosts.
openssl pkey -in eng/dev-keys/vendor-private.pem -pubout -out src/Hms.Web/vendor-public-key.pem
cp src/Hms.Web/vendor-public-key.pem src/Hms.Hr.Web/vendor-public-key.pem

sign() {
  # $1 = payload json, $2 = output path
  payload=$(mktemp)
  printf '%s\n' "$1" > "$payload"
  payload_b64=$(base64 < "$payload" | tr -d '\n')
  sig_b64=$(openssl dgst -sha256 -sign eng/dev-keys/vendor-private.pem -binary "$payload" | base64 | tr -d '\n')
  printf '{"payload":"%s","signature":"%s"}\n' "$payload_b64" "$sig_b64" > "$2"
  rm -f "$payload"
  echo "entitlement: $2"
}

# The ERP SKU: every module, with HR now licensed as part of the suite.
sign '{"customer":"Altushi General Hospital (DEV)","modules":["Registration","Appointments","Billing","Diagnostics","Lis","Dashboard","Admin","Notifications","Pharmacy","Ipd","Emr","Ot","Radiology","Hr"],"branches":1,"expiresUtc":"2028-07-01T00:00:00Z","graceDays":30}' \
  deploy/entitlements/dev-all-modules.json

# The HRM SKU. Admin ships in every SKU because a product with no user management is not a
# product (P29) — that is a bundling decision, not an accident.
sign '{"customer":"Meghna Textiles Ltd. (DEV)","modules":["Hr","Admin"],"branches":1,"expiresUtc":"2028-07-01T00:00:00Z","graceDays":30}' \
  deploy/entitlements/hrm-only.json

# Proves ADR-0026 choke point 2: loaded into the FULL ERP host, this licence must make /billing/*
# return 403 by URL — not merely hide it from the sidebar. Used by the entitlement endpoint test.
sign '{"customer":"Altushi General Hospital (DEV)","modules":["Hr","Admin"],"branches":1,"expiresUtc":"2028-07-01T00:00:00Z","graceDays":30}' \
  deploy/entitlements/hr-only-on-erp.json
