# Reproduzierbares, vorab optimiertes Keycloak-Image (Build-Optionen werden einmalig festgelegt).
# Version und Digest nur bewusst gemeinsam ändern; siehe docs/deployment.md (Keycloak-Update).
FROM quay.io/keycloak/keycloak:26.7.5@sha256:37dbaf6f0722c9ec246335f36e1ef8b2e6cb960f7c27e0d8c615121a3d475a85 AS builder

# http-relative-path ist eine Build-Option: Keycloak läuft intern und öffentlich unter /auth,
# damit Caddy es ohne Pfadumschreibung unter derselben Origin wie Küchenfuchs ausliefern kann.
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_HTTP_RELATIVE_PATH=/auth
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.7.5@sha256:37dbaf6f0722c9ec246335f36e1ef8b2e6cb960f7c27e0d8c615121a3d475a85
COPY --from=builder /opt/keycloak/ /opt/keycloak/

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
