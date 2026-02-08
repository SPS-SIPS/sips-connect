FROM node:24-slim AS builder
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM node:24-slim AS runner
WORKDIR /app

ENV NODE_ENV=production

COPY --from=builder /app/package.json ./package.json
COPY --from=builder /app/.next ./.next
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/public ./public

# Copy certs directory (for mounting at runtime)
COPY --from=builder /app/certs ./certs

# Copy custom HTTPS server and entrypoint
COPY server-https.js ./server-https.js
COPY entrypoint-https.sh ./entrypoint-https.sh
RUN chmod +x ./entrypoint-https.sh

EXPOSE 3000
# Entrypoint is overridden by docker-compose, but set CMD for local docker run
CMD ["/bin/sh", "/app/entrypoint-https.sh"]