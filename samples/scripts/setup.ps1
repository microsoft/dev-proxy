# login
Write-Output "Sign in to Microsoft 365..."
npx -p @pnp/cli-microsoft365 -- m365 login --authType browser

# create AAD app
Write-Output "Creating AAD app..."
$appId = npx -p @pnp/cli-microsoft365 -- "m365 aad app add --name Graph-Chaos-Proxy-Samples --multitenant --redirectUris http://localhost:5500/withsdk.html,http://localhost:5500/nosdk.html --apisDelegated https://graph.microsoft.com/User.Read.All,https://graph.microsoft.com/Presence.Read.All --platform spa --query appId -o text"

Write-Output "AppId: $appId"

# write app to env.js
Write-Output "Writing app to env.js..."
"const appId = '$appId';" | Out-File .\env.js

# write app to env_esm.js
"export const appId = '$appId';" | Out-File .\env_esm.js

Write-Output "DONE"
