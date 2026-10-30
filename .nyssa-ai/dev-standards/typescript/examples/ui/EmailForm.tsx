// Responsibility: Collect raw email input and deliver a validated address to its application caller.
// Public interface:
// +-- EmailForm({isSubmitting, onSubmit})
// `-- EmailFormProps
//     +-- isSubmitting
//     `-- onSubmit(email)
import {useState, type FormEvent, type ReactElement} from 'react';
import {toEmailAddress, type EmailAddress} from '../email/EmailAddress.js';
/** EmailFormProps supplies submission state and the application operation. */
export interface EmailFormProps {
  /** isSubmitting prevents duplicate submissions while the caller is working. */
  readonly isSubmitting: boolean;
  /** onSubmit receives an already validated address; persistence belongs to the caller. */
  readonly onSubmit: (email: EmailAddress) => void;
}
/** EmailForm delegates address validation and renders the resulting validation failure. */
export function EmailForm({isSubmitting, onSubmit}: EmailFormProps): ReactElement {
  const [input, setInput] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const parsed = toEmailAddress(input);
  function submit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault(); setSubmitted(true);
    if (parsed.ok && !isSubmitting) onSubmit(parsed.value);
  }
  return <form onSubmit={submit} noValidate>
    <label>Email<input value={input} onChange={event => setInput(event.target.value)} disabled={isSubmitting}/></label>
    {submitted && !parsed.ok && <p role="alert">{parsed.error}</p>}
    <button type="submit" disabled={isSubmitting}>Submit</button>
  </form>;
}
