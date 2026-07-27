#!/bin/sh
# Generates the DEV vendor keypair (private key stays out of git) and a signed
# all-modules entitlement file for local/demo use (ADR-0016).
# Production keys are vendor-held; this script never runs in production.
set -eu
cd "$(dirname "$0")/.."

mkdir -p eng/dev-keys deploy/entitlements

[ -f eng/dev-keys/vendor-private.pem ] || \
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out eng/dev-keys/vendor-private.pem 2>/dev/null

openssl pkey -in eng/dev-keys/vendor-private.pem -pubout -out src/Hms.Web/vendor-public-key.pem

payload=$(mktemp)
cat > "$payload" <<'JSON'
{"customer":"Altushi General Hospital (DEV)","modules":["Registration","Appointments","Billing","Diagnostics","Lis","Dashboard","Admin","Notifications","Pharmacy","Ipd","Emr","Ot","Radiology"],"branches":1,"expiresUtc":"2028-07-01T00:00:00Z","graceDays":30}
JSON

payload_b64=$(base64 < "$payload" | tr -d '\n')
sig_b64=$(openssl dgst -sha256 -sign eng/dev-keys/vendor-private.pem -binary "$payload" | base64 | tr -d '\n')

printf '{"payload":"%s","signature":"%s"}\n' "$payload_b64" "$sig_b64" \
  > deploy/entitlements/dev-all-modules.json
rm -f "$payload"
echo "entitlement: deploy/entitlements/dev-all-modules.json (signed, every entitled module)"
