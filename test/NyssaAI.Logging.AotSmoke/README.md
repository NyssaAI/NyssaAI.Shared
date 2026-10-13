# Logging native smoke application

This application writes structured records through two file providers with different minimum levels. It checks filtering, JSON fields, and disposal after Native AOT compilation, with reflection-based JSON serialization disabled.

From a Linux machine with the .NET 10 SDK and Native AOT prerequisites:

```sh
dotnet publish test/NyssaAI.Logging.AotSmoke -c Release -r linux-x64 -o .temp/logging-native
.temp/logging-native/NyssaAI.Logging.AotSmoke
```

The process exits nonzero on failure. The shared `test/verify-native.sh` script also runs the Repository native checks.
