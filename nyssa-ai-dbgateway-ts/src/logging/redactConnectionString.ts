// Responsibility: Redact sensitive passwords and tokens from database connection strings.
// Public interface:
// `-- redactConnectionString(connectionString)

export function redactConnectionString(connectionString: string): string {
  if (!connectionString) {
    return "";
  }
  let result = connectionString;
  // Redact password=... or Password=... in key-value connection strings
  result = result.replace(/(password\s*=\s*)([^;]+)/gi, "$1***");
  // Redact URI passwords: postgresql://user:password@host
  result = result.replace(/:\/\/([^:]+):([^@]+)@/g, "://$1:***@");
  return result;
}
