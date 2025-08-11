# REMINDER: Ensure this file is executable via "chmod +x watch.sh"
#!/bin/bash
set -o allexport
source .env
set +o allexport
dotnet watch
