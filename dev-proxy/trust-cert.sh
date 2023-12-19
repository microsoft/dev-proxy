set -e

echo -e "\nDev Proxy uses a self-signed certificate to intercept and inspect HTTPS traffic.\nUpdate the certificate in your Keychain so that it's trusted by your browser? (Y/n)? \c"
read -n 1 answer

if [ "$answer" = "n" ]; then
    echo -e "\n\033[1;33mTrust the certificate in your Keychain manually to avoid errors.\033[0m\n"
    exit 1
fi

echo -e "\n"

cert_name="Dev Proxy CA"
# export cert from keychain to PEM
echo "Exporting Dev Proxy certificate..."
security find-certificate -c "$cert_name" -a -p > dev-proxy-ca.pem
# add trusted cert to keychain
echo "Updating Dev Proxy trust settings..."
security add-trusted-cert -r trustRoot -k ~/Library/Keychains/login.keychain-db dev-proxy-ca.pem
# remove exported cert
echo "Cleaning up..."
rm dev-proxy-ca.pem
echo -e "\033[0;32mDONE\033[0m\n"