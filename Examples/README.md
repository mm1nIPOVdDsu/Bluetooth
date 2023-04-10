# Introduction
Examples of connecting to and interacting with various Bluetooth Classic profiles for a phone. All examples are intentionally verbose to provide the most amount of detail in how interacting with a profile works. Once it is understood how the communication works to a device, it can be abstracted out.

The specifications used by iOS and Android for MAP (v1.1) and PBAP (v1.2) are rather old and will be deprecated soon. All examples are using the currently used specifications for iOS and Android.

- PBAP 
  - Version 1.2
  - Deprecation Date: February 1 2021
  - Withdrawal Date: February 1 2024
  - Possible next version: 1.2.3
- MAP 
  - Vesrion 1.1
  - Deprecation Date: February 1 2023
  - Withdrawal Date: February 1 2024
  - Possible next versions: 1.2.2, 1.3.1, or 1.4.2
 
# Getting Started
- Dependencies
  -  .NET 6
  -  Windows 10.0.19041.0 minimum

## Phonebook Profile
The phonebook example connects to a device, requests access to the "phonebook access profile" (PBAP), pulls contacts, then disconnects.

### Steps
- Connect to a device
- Connect to device's Phonebook Access Profile
- Pull contacts from connected device

NOTE: this is still a work in progress as there is an issue parsing a response message in the PBAP response I believe.

## Message Profile
Working on it.

# Helpful Links
## Bluetooth
 - Specifications: https://www.bluetooth.com/specifications/specs/?types=specs-docs&keyword=&filter=
 - Apple Supported Profiles: https://support.apple.com/en-us/HT204387

# FAQ

## Why is it taking so long for my phone to get a pairing request/Why am I getting errors connecting to my device?
If you've been pairing/unpairing the device multiple times, your phone/computer can get a little stupid. The Windows stack for Bluetooth isn't the best. Disabling/enabling Bluetooth on the phone and/or your computer typically works.

## Why is it failing when requesting access to a device profile?
This is more a problem with iOS than Android as Android will pop a notification for a profile request. Apple like being different so they only prompt if you're using BLE and their special way of connecting. To help remedy this, have be in the Bluetooth settings menu when connecting, go into and back out of details (the (i) thing). Eventaully the permissions button will appear.
