// Responsibility: Display normalization, both output representations and expected validation failure.
// Public interface:
// (none)
import {toEmailAddress} from './email/EmailAddress.js';
import {emailFormatterText} from './formatting/emailFormatterText.js';
import {emailFormatterJson} from './formatting/emailFormatterJson.js';
const parsed = toEmailAddress(' Ada@EXAMPLE.COM ');
if (!parsed.ok) throw new Error('sample prerequisite failed');
console.log(`normalized: ${parsed.value}`);
for (const formatter of [emailFormatterText(), emailFormatterJson()]) console.log(formatter.format(parsed.value));
const invalid = toEmailAddress('not-an-address');
if (invalid.ok) throw new Error('expected validation failure was missing');
console.log(`rejected: ${invalid.error}`);
