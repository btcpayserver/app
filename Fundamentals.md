# BTCPay ~~Server~~ App

## Introduction

BTCPay Server is an incredibly successful self-hosted, free, open-source payment processor for Bitcoin. 
It allows anyone to install in on a server and start accepting payments with no middlemen.
Setting up a bitcoin payment method is relatively simple, you can import an existing one or you can generate a new one, but there is absolutely no need to expose your private keys to the server.
This enabled BTCPay Server to become a multi-merchant multi-store solution with minimal trust required. 
But Bitcoin payments in the traditional sense are currently not very feasible for many commerce use-cases with today's realities. 

Enter the Lightning Network.
BTCPay Server has supported Lightning Network payments since 2018, and it has been a game-changer for many merchants.
It allows for instant, low-fee payments, and it is a perfect fit for many commerce use-cases. 
Our support is built through a flexible abstraction layer, BTCPayServer.Lightning, which allows us to support multiple Lightning Network implementations.
In fact, we support all, including virtual ones like Strike, Blink, or remote interfaces that also act as another abstraction layer like LNDHub or Nostr Wallet Connect.

However the Lightning Network comes with a set of challenges. It introduces a requirement for private keys to be constantly available, which is a security and regulatory risk for shared server operators.
It also introduces a new set of challenges for the user experience, such as the need to manage channels, liquidity, and the need to be online to receive payments.
Backups are also a challenge, as they are not as simple as backing up a single private key and requires constant state updates for every operation.

## Our Visions

We believe we can ease these challenges by providing a seamless experience for merchants, in a way that merchants can experience Lightning closer to the ease of on-chain wallet management.
We are building a new product, BTCPay App, which is a cross-platform application that has two main goals:
* a smooth and rapid onboarding user interface for merchants to accept and manage payments in-person
* a seamless experience for merchants to enable the use of Lightning Network for their stores through a custom implementation of Lightning.

## The User Interface

The user interface is designed to be simple and intuitive, with a focus on the most common operations a merchant would need to perform.
It is designed to be used in-person, with a focus on mobile devices, but it can also be used on desktops.
The onboarding experience is meant to be as smooth as possible, with minimal configuration required to start accepting payments.
Shared server operators, which we call "Ambassadors", can direct users to this application through invitation links, that will install, and automatically configure all necessary settings to start accepting payments.

The user interface is built using Blazor. It allows us to build re-usable components that can be re-used across all platforms and also BTCPay Server.
The host application for desktop is Photino for desktop, Maui for mobile, and Blazor Server for web access.

## The Lightning Node

Built using LDK (Lightning Development Kit), our custom implementation of Lightning is designed to be a seamless experience for merchants.
It is designed to be a non-custodial solution, where the merchant generates an onchain wallet and subsequently a lightning node on-device.
BTCPay Server is used as a backend to provide the necessary information for the node to function. 
The node utilizes the aforementioned abstraction layer to communicate with BTCPay Server,and hooks into the payment processing flow 
The node stores and backs up all its data inside BTCPay Server against a user account.

## Backup
Using the onchain mnemonic seed, we derive a private key, that is used as an encryption key. This encryption key is used to encrypt all backup data before transporting it to BTCPay Server.
The backup data is stored in a way that it can be easily restored on any device, and it is encrypted in a way that only the user can decrypt it.
Therefore, when you are restoring your backup, you will need to provide the mnemonic seed, or the derived private key to decrypt the backup data.

All data that is backed up is versioned. When an item is created, it is assigned a version of  0. When an item is updated, the version is incremented by 1. When an item is deleted, the version is incremented by 1. 
The backups on BTCPay Server also store the version to ensure no older version of the data is uploaded. Only the latest variant is persisted.

WHen data that is meant to be backup up is persisted, an SQL trigger is used to create an outbox record. This record is then picked up by the backup service, and the data is uploaded to BTCPay Server.

## Synchronization 
Since the backup can be restored and is constantly updated, a user may have multiple devices paired to the same user account of BTCPay Server. 
This means this user is essentially running the same node on multiple devices. However, this is not possible, and any attempt to do this will result in a catastrophic failure and loss of funds.
To prevent this, we are using a master/slave model, where only one device can be the master, and all other devices are slaves at any given moment.
The master device is the one that can operate the wallet, and is the only one that can update the backup state on BTCPay Server. All other devices are slaves, and they can only read the backup state, and they do this occassionally.
Every app instance has a unique identifier, and this is used by BTCPay Server to determine who the current master is. A master device may only switch if the current master device signals to BTCPay Server that it is no longer the master. By doing so, it implies that the server has received the latest state of the backup, and it is safe to switch to another device.
On load of any app instance, upon connecting to BTCPay Server, the app will first synchronize all data from the server, and then attempt to become the master.
If for some reason the app is unable to maintain a connection while being the master, it can send the  backup state and use that as a signal to not be the master any more.

A slave device may not be able to directly operate a wallet, but it can still act as an interface to these operations. For example, a slave device can still generate invoices, process refunds, and view balances. These commands are simply proxies to the master device, which will execute them on behalf of the slave device.

## Connectivity
BTCPay App utilizes Signalr, a real-time communication library, to communicate with BTCPay Server. This allows for real-time two-way updates.
It also utilizes the Greenfield API, the all-inclusive api for BTCPay Server. For backups, we use a VSS compatible REST API, allowing future work to backup the lightning node to various other services.

## Channels
We envision that Ambassadors will run a lightning node as part of the Server, and will offer channels to its users. BTCPay Server notifies the app upon connection of its lightning node, so that they may establish a persistent connection. We believe ambassdaors will offer inbound channels to its users.
We've also introduced LSP support, notably JIT channel creation, which allows the app to generate payment requests routes through LSPs who will automate channel management on demand. We also plan to have ahead of time channel purchases to provide upfront, cheaper liquidity to the user.