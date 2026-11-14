# MEEDO reply on advance payment, arrears carry-over and added fees — 2026-11-11

**Source:** Direct chat with the Cantilan MEEDO office, relayed by the project owner on 2026-11-14 with a screenshot. Question sent Tuesday 2026-11-10, 11:04 PM; reply Wednesday 2026-11-11, 8:31 AM. The owner states no further question can be sent.
**Authority:** Direct Cantilan office clarification, for the INF-4D panel compliance (Duero, Maligsa, Flores).

## Question (owner, in Cebuano/Surigaonon)

> pwede ba ang overcharges/advanced payment po? then sa Arrears/Delinquent po if kong naa pobay additional fee sa jaun Arrears/Deliunquent kay want ipa automate pag calculate once Arrears/Delinquent na sya.

Meaning: are overcharges or advance payment allowed? And is there an additional fee on Arrears or Delinquent accounts, so the system can calculate it automatically once an account is Arrears or Delinquent?

## Reply (MEEDO, verbatim)

> advance payment sa stall panyagsa da jaun sir, arrears/delinquent stall na waya maka bajad carry over to january

Reading used by StallTrack (owner to correct if the wording means otherwise):

1. **Advance payment** of stall rent happens there, though only occasionally ("panyagsa" read as *panagsa*, seldom). It is an accepted practice, not refused.
2. **Arrears/Delinquent:** a stall that has not paid carries the unpaid amount over to January, into the next year.
3. **Added fee:** the reply names none. The answer to "is there an additional fee" is that the balance carries over.
4. **Overcharges:** not answered.

## What StallTrack does with it

| Point | System behaviour | Change needed |
|---|---|---|
| Advance payment accepted | The office can take rent ahead up to the Head's limit; the portal one month at a time; collectors cannot (IA-071) | None. IA-071's advance payment now has office support |
| Unpaid rent carries over to January | Unpaid months stay owed in their own billing month and show as previous balance; from 1 January, unpaid rent of an earlier year is **Arrears** (`ReceivableClassification`, IA-069) | None |
| No added fee named | No penalty is created until a Head enters a rate with its governing reference (IA-072). Cantilan has none, so Cantilan charges none | None. Keep Cantilan without a late-fee rate |
| Overcharges | Office and collectors refuse excess; an online capture that differs is held for the Head (2.1, 2.6 in the tracking document) | None. Stays a StallTrack Rule |
