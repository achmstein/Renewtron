FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .

# Frontend uses same-origin relative paths (/api, /health, /hangfire) so this is
# only a hint exposed to the bundle for any code that reads import.meta.env.
ARG VITE_API_URL=https://businessnames.applyforanabn.au
ENV VITE_API_URL=$VITE_API_URL

RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
# busybox wget resolves `localhost` to ::1 first, but nginx binds IPv4 only —
# probe the explicit IPv4 loopback or the check reports unhealthy forever.
HEALTHCHECK --interval=30s --timeout=3s CMD wget -q -O- http://127.0.0.1/ >/dev/null || exit 1
