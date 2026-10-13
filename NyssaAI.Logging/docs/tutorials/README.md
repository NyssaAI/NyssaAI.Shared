# Logging tutorials

These tutorials show how an application uses `ILogger<T>` with NyssaAI's file destination. Each test creates its own temporary workspace, registers the production logging extensions, writes records, and checks the resulting file.

The examples are in [LoggingTutorials.cs](../../../test/NyssaAI.Logging.UseCaseTests/LoggingTutorials.cs).

| Tutorial | What it demonstrates |
| --- | --- |
| `Write_Logs_To_A_Json_File` | Add the file destination to Microsoft logging and inject `ILogger<T>`. |
| `Configure_Logging_In_One_Call` | Register standard error and a file with `AddNyssaLogging`. |
| `Log_To_Standard_Error_Only` | Use the default destination without creating a log file. |
| `Log_Structured_Messages` | Record an event name and separately searchable values. |
| `Choose_Which_Records_Reach_The_File` | Set a higher minimum level for the file. |
| `Keep_Running_When_The_Log_File_Cannot_Be_Opened` | Keep a working destination when another file cannot be opened. |

Run all tests from the repository root with `./run-tests.ps1`. To run just these tutorials after building, execute:

```powershell
dotnet run --project test/NyssaAI.Logging.UseCaseTests --no-build
```

The [main README](../../README.md) explains file paths, record fields, retention, and failure behavior.
