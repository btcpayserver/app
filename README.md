# BTCPay App

## Setup for development

Here's what needs to happen to run the app in the browser:

```bash
# Clone the app repo
git clone git@github.com:btcpayserver/app.git

# Switch to it
cd app

# Initialize the server submodule
git submodule init && git submodule update --recursive

# Install the workloads
dotnet workload restore

# Go to the server submodule
cd submodules/btcpayserver/BTCPayServer.Tests

# Verify you are on the `mobile-working-branch` branch
git branch --show current

# If not, check it out
git checkout mobile-working-branch

# Run the server
docker-compose up dev
```

Now you can open up the IDE and run `DEV ALL` profile which builds both the App and the BTCPay Server.

The app should open in the browser and you should see the Welcome screen.
Click the Connect button, use `http://localhost:14142` as the server URL and an existing account for the server.

## Troubleshooting

After the first run of `DEV ALL` on a Linux machine with a new .NET setup, you may run into the [dotnet dev-certs - Untrusted Root](https://github.com/dotnet/aspnetcore/issues/41503)
error, and you may find a solution at the [following link](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)

## Missing dependencies in development environment

If BTCPay Server does not start up with the app and its dependencies, run this:

```bash
cd BTCPayApp.Core
dotnet publish /p:RazorCompileOnBuild=true -o ../BTCPayServer.Plugins.App/bin/Debug/net8.0
cd -
cd BTCPayServer.Plugins.App
dotnet publish /p:RazorCompileOnBuild=true -o bin/Debug/net8.0
cd -
```

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
