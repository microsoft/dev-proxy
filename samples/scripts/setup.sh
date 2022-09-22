#!/usr/bin/env bash

# login
echo "Sign in to Microsoft 365..."
npx -p @pnp/cli-microsoft365 -- m365 login --authType browser

# create AAD app
echo "Creating AAD app..."
appId=$(npx -p @pnp/cli-microsoft365 -- m365 aad app add --name "Microsoft Graph Chaos Proxy Samples" --multitenant --redirectUris "http://localhost:5500/withsdk.html,http://localhost:5500/nosdk.html" --apisDelegated "https://graph.microsoft.com/User.Read.All,https://graph.microsoft.com/Presence.Read.All" --platform spa --query "appId" -o text)

# write app to env.js
echo "Writing app to env.js..."
echo "const appId = '$appId';" > env.js

# write app to env_esm.js
echo "export const appId = '$appId';" > env_esm.js

echo "DONE"
