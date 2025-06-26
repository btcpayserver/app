# BTCPay App

## Setup for development

Here's what needs to happen to run the app in the browser:

```bash
# Clone the app repo
git clone git@github.com:btcpayserver/app.git

# Switch to it
cd app

# Run the setup script
./setup.sh

# Go to the server submodule
cd submodules/btcpayserver/BTCPayServer.Tests

# Run the server
docker-compose up dev
```

Now you can open up the IDE and run `DEV ALL` profile which builds both the App and the BTCPay Server.

The app should open in the browser and you should see the Welcome screen.
Click the Connect button, use `https://localhost:14142` as the server URL and an existing account for the server.

## Lightning Channels

To establish channels for local testing, you can use the Docker Lightning CLI scripts like this:

```bash
# The scripts are inside the submodule's test directory
cd submodules/btcpayserver/BTCPayServer.Tests

# Run the general channel setup for the BTCPay LN nodes
./docker-lightning-channel-setup.sh
```

Besides establishing channel connections between the various BTCPay LN testing nodes, this will also give you their node URIs.

### Create channel

- App: Go to the [Lightning Channels view](https://localhost:7016/settings/lightning/channels) and connect to one of the peer node URIs from the comand above
- App: Open Channel to that peer
- CLI: Run `./docker-bitcoin-generate.sh 5`
- App: Refresh the Lightning Channels view and see if the channel got confirmed
- App: Go to the [Send view](https://localhost:7016/send) and see if your Lightning local/outbound capacity is present

### Send payment

- CLI: Generate an invoice with the peer script, e.g. `./docker-customer-lncli.sh addinvoice --amt 10000`
- App: Pay the Lightning invoice on the Send view

## Troubleshooting

### Development certificates

After the first run of `DEV ALL` on a Linux machine with a new .NET setup, you may run into the [dotnet dev-certs - Untrusted Root](https://github.com/dotnet/aspnetcore/issues/41503)
error, and you may find a solution at the [following link](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)

### GrapheneOS

If you are using GrapheneOS for the Android development, make sure to explicitely [enable code debugging](https://discuss.grapheneos.org/d/8330-app-compatibility-with-grapheneos).
To run the app with debugger attached, the BTCPay app needs to get explicitely set as debug app in `Settings > System > Developer Settings > Debugging > Set Debug App`.

### Sunmi V2s

To enable developer mode on the POS device, go to `Settings > About device` and tap the `Build number` list item seven times. It will conmfirm "You are now a developer" and afterwards you will find `Settings > System > Developer options` being present. There you can turn on USB debugging and select BTCPay app for debugging.
