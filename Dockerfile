FROM node:24-slim AS builder

WORKDIR /app

COPY package.json package-lock.json ./

RUN npm install

COPY . .

FROM builder AS runner

EXPOSE 3000

CMD ["npm", "run", "dev"]
