#!/usr/bin/env bash

# Utils
checkWorkdir() {
  git fetch
  status=$(git status --porcelain)
  if [ ! -z "${status}" ]; then
    printf "Working directory is not clean:\n\n${status}\n\n"
    exit 1
  fi
}

checkBranch() {
  expected=$1
  branch=$(git branch --show-current)
  if [ -z "${branch}" ]; then
    checkWorkdir
    git checkout "${expected}"
    branch=$(git branch --show-current)
  fi
  if [ "${branch}" != "${expected}" ]; then
    printf "Not on ${expected} branch: ${branch}\n\n"
    exit 1
  fi
}

commitAndTag() {
  tagName=$1
  git add .
  git commit -a -m "${tagName}"
  git tag "${tagName}" -m "${tagName}"
  git push
  git push --tags
}

# Configuration
version=$1
name="BTCPayServer.Plugins.App"
csproj="${name}/${name}.csproj"

# Setup
if [ ! -z "${version}" ]; then
  checkBranch "master"
  checkWorkdir
fi

if [ ! -f ./submodules/btcpayserver/BTCPayServer.PluginPacker/artifacts/PluginPacker/BTCPayServer.PluginPacker ]; then
  printf "\n=====> Create plugin packer\n\n"
  cd ./submodules/btcpayserver/BTCPayServer.PluginPacker
  mkdir -p artifacts/PluginPacker
  dotnet build -c Release -o artifacts/PluginPacker
  rm -rf artifacts/btcpayserver
  cd -
fi

if [ ! -z "${version}" ]; then
  printf "\n=====> Update version to $version\n"
  sed -i "s%<Version>.*</Version>%<Version>$version</Version>%g" $csproj
fi
ver=$(grep -oP '(?<=<Version>)(.*)(?=</Version>)' $csproj)

printf "\n=====> Build BTCPayApp.Core\n\n"

cd BTCPayApp.Core
rm -rf ./tmp/**
dotnet publish -c Release -o "tmp/publish" /p:RazorCompileOnBuild=true
cd -

printf "\n=====> Build BTCPayServer.Plugins.App\n\n"

cd BTCPayServer.Plugins.App
rm -rf ./tmp/**
dotnet publish -c Release -o "tmp/publish" /p:RazorCompileOnBuild=true
cd -

printf "\n=====> Add BTCPayApp.Core files to BTCPayServer.Plugins.App\n"
cp -r ./BTCPayApp.Core/tmp/publish/* ./BTCPayServer.Plugins.App/tmp/publish

printf "\n=====> Pack plugin in ./BTCPayServer.Plugins.App/tmp/$ver \n"
./submodules/btcpayserver/BTCPayServer.PluginPacker/artifacts/PluginPacker/BTCPayServer.PluginPacker "./BTCPayServer.Plugins.App/tmp/publish" "$name" "./BTCPayServer.Plugins.App/tmp/publish-package"
mkdir -p ./BTCPayServer.Plugins.App/tmp/$ver && cp ./BTCPayServer.Plugins.App/tmp/publish-package/$name/$ver/* ./BTCPayServer.Plugins.App/tmp/$ver && rm -rf ./BTCPayServer.Plugins.App/tmp/publish*

if [ ! -z "${version}" ]; then
  printf "\n=====> Commit and tag\n\n"
  commitAndTag "${name} v${version}"
fi
