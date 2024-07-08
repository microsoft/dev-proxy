$versionString = "v0.20.0-beta.1"
$version = $versionString.Substring(1)

Remove-Item ../bld -Recurse -Force

dotnet publish ../dev-proxy/dev-proxy.csproj -c Release -p:PublishSingleFile=true -r win-x64 --self-contained -o ../bld -p:InformationalVersion=$version
dotnet build ../dev-proxy-plugins/dev-proxy-plugins.csproj -c Release -r win-x64 --no-self-contained -p:InformationalVersion=$version
cp -R ../dev-proxy/bin/Release/net8.0/win-x64/plugins ../bld
pushd

cd ../bld
Get-ChildItem -Filter *.pdb -Recurse | Remove-Item
Get-ChildItem -Filter *.deps.json -Recurse | Remove-Item
Get-ChildItem -Filter *.runtimeconfig.json -Recurse | Remove-Item
popd