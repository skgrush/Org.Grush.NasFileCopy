# Org.Grush.NasFileCopy

> CLI tool for mounting and copying TrueNAS Scale datasets to USB devices.

**WARNING: This is not and should not be taken as a production- or safe-tool. This is a toy utility project.**

## TODO: Installing from server
```sh
mkdir /opt
cd /opt

curl -L https://github.com/skgrush/Org.Grush.NasFileCopy/releases/download/v0.0.1-rc.10/Org.Grush.NasFileCopy.ServerSide_linux-x64.zip -o /tmp/tmp-NasFileCopy.zip \
  && sudo unzip /tmp/tmp-NasFileCopy.zip -d /var \
  && sudo chmod o+x /var/Org.Grush.NasFileCopy.ServerSide
rm /tmp/tmp-NasFileCopy.zip

chmod o+x Org.Grush.NasFileCopy.ServerSide
```

## Installation

1. SSH into TrueNAS as a sudo-able or in the WebUI use `System Settings > shell`
2. TODO: Install application to /opt/Org.Grush.NasFileCopy.ServerSide
3. Create a TrueNAS group with `Allowed sudo commands with no password` set to `/opt/Org.Grush.NasFileCopy.ServerSide`
4. Create or modify your TrueNAS user with
   - the above group added to their group
   - a home directory set to a real directory (I created a dataset just for user-homes)
   - SSH password login enabled (TODO: support keys)
   - shell set to `zsh`

## Running the application

### Listing devices

```sh
/opt/NasFileCopy list
```

### Copying

#### On server

Replace `$USBLABEL` the user-facing label of the USB partition.
Replace `$DATASET` with the FULL dataset path, e.g. `rootDataset/targetDataset`.

```sh
sudo /opt/Org.Grush.NasFileCopy.ServerSide copy --destination-device-label=$USBLABEL --source-name=$DATASET
```

If the copy fails it will tell you the viable destination labels and source names, depending on what you got wrong.

#### From a remote client

```sh
./Org.Grush.NasFileCopy.ClientSide.Cli[.exe]
```

TODO:
Current implementation prompts you for hostname, credentials, and method (copy or list), then executes the operation on the remote.
