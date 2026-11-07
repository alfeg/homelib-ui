# homelib-gate: iroh endpoint in front of the HomeLib HTTP server.
FROM rust:1.95-alpine AS build
# iroh is a big build; capped so `docker compose build` on a small home server does not
# starve everything else running there of memory.
ARG CARGO_BUILD_JOBS=2
ENV CARGO_BUILD_JOBS=${CARGO_BUILD_JOBS}
RUN apk add --no-cache musl-dev
WORKDIR /src
COPY . .
RUN cargo build --release -p homelib-gate

FROM alpine:3.22
COPY --from=build /src/target/release/homelib-gate /usr/local/bin/homelib-gate
ENTRYPOINT ["homelib-gate"]
CMD ["serve"]
