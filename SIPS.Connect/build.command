docker buildx use multiarch-builder
docker buildx create --name multiarch-builder --use
docker buildx build --platform linux/amd64,linux/arm64 -t hanad/sips-connect:2.0.0 --push -f Dockerfile ..