docker buildx create --name multiarch-builder --use
docker buildx build --platform linux/amd64,linux/arm64 -t hanad/sips-connect:1.6.7 --push -f Dockerfile ..