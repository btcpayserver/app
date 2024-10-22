# BTCPay App

## Setup for development

Here's what needs to happen to run the app in the browser:

```bash
# Clone the app repo
git clone git@github.com:btcpayserver/app.git

# Switch to it
cd app

# Install the workloads
dotnet workload restore

# Initialize the server submodule
git submodule init && git submodule update --recursive

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
