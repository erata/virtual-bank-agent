#!/bin/sh
# Renders the header snippet (it needs KEYCLOAK_PUBLIC_URL) into a writable location before nginx starts.
# The main server block is rendered by the image's own 20-envsubst-on-templates.sh.
set -eu
: "${KEYCLOAK_PUBLIC_URL:?KEYCLOAK_PUBLIC_URL is required}"
mkdir -p /tmp/nginx-snippets
envsubst '${KEYCLOAK_PUBLIC_URL}' < /etc/nginx/snippet-templates/security-headers.conf.template > /tmp/nginx-snippets/security-headers.conf
cp /etc/nginx/snippet-templates/proxy.conf /tmp/nginx-snippets/proxy.conf
