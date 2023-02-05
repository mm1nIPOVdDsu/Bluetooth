# Introduction
Examples of connecting to and interacting with various Bluetooth Classic profiles for a phone. All examples are intentionally verbose to provide the most amount of detail in how interacting with a profile works. Once it is understood how the communication works to a device, it can be abstracted out.

# Getting Started
- Dependencies
  -  .NET 6
  -  Windows 10.0.19041.0 minimum

# Examples


## Phonebook Profile
The phonebook example connects to a device, requests access to the "phonebook access profile" (PBAP), pulls contacts, then disconnects.

### Steps
- Connect to a device
- Connect to device's Phonebook Access Profile
- Pull contacts from connected device

NOTE: this is still a work in progress as there is an issue parsing a response message in the PBAP response I believe.

## Message Profile
Working on it.

- Drew
