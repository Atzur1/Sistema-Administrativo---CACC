FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Public URL, compiled into the browser bundle; never pass secrets as build args.
ARG DEPORTIVO_URL
RUN node -e "const fs=require('node:fs');const url=process.env.DEPORTIVO_URL;if(!url || !/^https?:\/\//.test(url))throw Error('DEPORTIVO_URL must be an absolute HTTP(S) URL');fs.writeFileSync('src/environments/environment.prod.ts','export const environment = '+JSON.stringify({production:true,apiUrl:'/api',deportivoUrl:url})+';\n');"
RUN npm run build -- --configuration production --output-path dist/container

FROM nginx:stable-alpine AS runtime
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/container/browser /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s CMD wget -q -O /dev/null http://127.0.0.1/healthz || exit 1
