# syntax=docker/dockerfile:1
# Build the Angular app, then serve it with an unprivileged nginx that also proxies /api to the API.

FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.27-alpine
# Defaults for `make up`; compose sets them explicitly.
ENV KEYCLOAK_PUBLIC_URL=http://localhost:8080 \
    KEYCLOAK_REALM=virtual-bank \
    KEYCLOAK_UI_CLIENT=virtual-bank-ui \
    API_UPSTREAM=api:8080 \
    NGINX_ENVSUBST_FILTER="^(KEYCLOAK_|API_UPSTREAM)"
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/security-headers.conf.template nginx/proxy.conf /etc/nginx/snippet-templates/
COPY --chmod=0755 nginx/40-render-snippets.sh /docker-entrypoint.d/40-render-snippets.sh
COPY --from=build /src/dist/virtual-bank-ui/browser /usr/share/nginx/html
EXPOSE 8080
