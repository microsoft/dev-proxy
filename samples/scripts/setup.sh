#!/usr/bin/env bash

# login
echo "Sign in to Microsoft 365..."
npx -p @pnp/cli-microsoft365 -- m365 login --authType browser

# create AAD app
echo "Creating Entra app..."
appId=$(npx -p @pnp/cli-microsoft365 -- m365 entra app add --name graph-developer-proxy-samples --multitenant --redirectUris http://localhost:3000/withsdk.html,http://localhost:3000/nosdk.html --apisDelegated https://graph.microsoft.com/User.Read.All,https://graph.microsoft.com/Presence.Read.All --grantAdminConsent --platform spa --query appId -o text)

# write app to env.js
echo "Writing app to env.js..."
echo "const appId = '$appId';" > ./env.js

echo "DONE"
