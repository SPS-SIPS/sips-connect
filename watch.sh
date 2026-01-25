#!/bin/bash
# REMINDER: Ensure this file is executable via "chmod +x watch.sh"
set -o allexport
source .env.local
set +o allexport
dotnet watch
