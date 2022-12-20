# Samples

## Table of Contents

- [Pre-requisites](#prereqs)
- [Configure Azure AD App Registration](#appreg)
    - [PowerShell 7](#pwsh)
    - [Bash](#bash)
    - [Manual](#manual)
- [Launch Sample](#launch)

## <a id="prereqs">Pre-requisites</a>

All the samples have been designed with keeping dependencies to an absolute minimum, however there are a few things that you will need.

You will need a Microsoft 365 Tenant and be able to use an account that has permissions to create an Azure AD App Registrations in your tenant.

We highly recommend that you use a Microsoft 365 Developer Tenant with content packs installed when testing these samples, you can create a developer tenant by [signing up to the Microsoft 365 Developer Program](https://aka.ms/m365/).

## <a id="appreg">Configure Azure AD App Registration</a>

There are two ways which you can configure the App Registration required for the samples to work correctly, through automatation using either a `bash` or `pwsh` script we provide for you in the `scripts` directory, or manually through Azure Portal.

### <a id="pwsh">PowerShell 7</a>

> The script uses CLI for Microsoft 365 to authenticate with and create the app registration in your tenant, therefore requires nodejs, v8 or greater to be installed

```sh
PS > cd ./samples/
PS > ./scripts/setup.ps1
```

Follow the prompts in the terminal.

### <a id="bash">bash</a>

> The script uses CLI for Microsoft 365 to authenticate with and create the app registration in your tenant, therefore requires nodejs, v8 or greater to be installed

```sh
$ > cd ./samples/
$ > chmod +x /scripts/setup.sh
$ > ./scripts/setup.sh
```

Follow the prompts in the terminal.

### <a id="manual">Manual</a>

The following table provides details of how to configure your app registration.

| Property | Value |
| ---- | ---- |
| Name | graph-developer-proxy-samples |
| Account types | Accounts in any organizational directory (Any Azure AD directory - Multitenant) |
| Platform type | Single-page application |
| Redirect URIs | http://localhost:3000/withsdk.html <br> http://localhost:3000/nosdk.html |
| API Permissions | Microsoft Graph <br> User.Read.All (Delegate) w/ Admin Consent <br> Presence.Read.All |

After creating the app registation, create a file called `env.js` in the root of the `samples` directory with the following contents, replacing `<clientid>` with the value which can be copied from the portal.

```
const appId = '<clientid>';
```

## <a id="launch">Launch Sample</a>

```sh
$ > cd ./samples/
$ > npx lite-server
```

![Samples](img/samples.png)
