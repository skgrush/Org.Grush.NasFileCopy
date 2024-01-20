# Org.Grush.NasFileCopy

> CLI tool for mounting and copying TrueNAS Scale datasets to USB devices.

**WARNING: This is not and should not be taken as a production- or safe-tool. This is a toy utility project.**

## Installation

1. SSH into TrueNAS as a sudo-able or in the WebUI use `System Settings > shell`
2. To get the .NET installer, run
   ```sh
   mkdir /tmp/dotnet && cd /tmp/dotnet
   wget https://dot.net/v1/dotnet-install.sh -O ./dotnet-install.sh
   chmod +x ./dotnet-install.sh
   ```
3. Install the .NET 8+ runtime. You can change this to whatever you see fit based on the [dotnet-install reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) but here's my recommendation:
   ```sh
   sudo mkdir -m 777 /opt/dotnet
   ./dotnet-install.sh --channel LTS --runtime dotnet --install-dir /opt/dotnet
   ```
   You can also add `--dry-run` to dry run it.
4. either in your personal shell rc profile or in `/etc/zsh/zshenv` add
   ```sh
   export DOTNET_ROOT=/opt/dotnet
   ```
5. TODO: Install application to /opt/NasFileCopy

## Running the application

### Listing devices

```sh
/opt/NasFileCopy list
```

### Copying

Replace `$USBLABEL` the user-facing label of the USB partition.
Replace `$DATASET` with the FULL dataset path, e.g. `rootDataset/targetDataset`.

```sh
sudo -E /opt/NasFileCopy copy --destination-device-label=$USBLABEL --source-name=$DATASET
```

Currently we have to use `sudo -E` to maintain the `DOTNET_ROOT` env var.