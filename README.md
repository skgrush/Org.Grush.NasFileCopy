# Org.Grush.NasFileCopy

> CLI tool for mounting and copying TrueNAS Scale datasets to USB devices.

**WARNING: This is not and should not be taken as a production- or safe-tool. This is a toy utility project.**

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

Replace `$USBLABEL` the user-facing label of the USB partition.
Replace `$DATASET` with the FULL dataset path, e.g. `rootDataset/targetDataset`.

```sh
sudo -E /opt/NasFileCopy copy --destination-device-label=$USBLABEL --source-name=$DATASET
```

If the copy fails it will tell you the viable destination labels and source names, depending on what you got wrong.