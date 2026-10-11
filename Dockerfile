FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

FROM caddy:2.11.7-alpine@sha256:d8542f48d34a9cf4e4c11a478865229840e87e4c96ea3f439101f31a5d35f75f
COPY --from=frontend-build /src/frontend/dist/frontend/browser/ /srv/
COPY Caddyfile /etc/caddy/Caddyfile
