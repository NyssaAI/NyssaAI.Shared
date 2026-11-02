const fs = require("node:fs");
const path = require("node:path");

const dtsPath = path.resolve(__dirname, "../dist/index.d.ts");
if (!fs.existsSync(dtsPath)) process.exit(0);

let dts = fs.readFileSync(dtsPath, "utf8");
dts = dts.replace(/export declare const ErrorCategory: \{[\s\S]*?\};/, 'export { ErrorCategory } from "./ErrorCategory.js";');
dts = dts.replace(/export declare const Error: typeof ErrorClass;/, 'export { Error } from "./Error.js";');
dts = dts.replace(/export declare const ResultException: typeof ResultExceptionClass;/, 'export { ResultException } from "./ResultException.js";');
dts = dts.replace(/export declare const Ok: typeof OkClass;/, 'export { Ok } from "./Ok.js";');
dts = dts.replace(/export declare const Err: typeof ErrClass;/, 'export { Err } from "./Err.js";');
dts = dts.replace(/export declare const Result: Readonly<[\s\S]*?>;/, 'export { Result } from "./Result.js";');
dts = dts.replace(/export declare const Guard: typeof GuardClass;/, 'export { Guard } from "./Guard.js";');

fs.writeFileSync(dtsPath, dts);
console.log("Successfully normalized dist/index.d.ts exports");
