# Physical network recovery timing

This optional observer has software validation. A complete physical end-to-end workflow remains separately required; installing this SDK does not establish that evidence.

A network recovery deadline starts when network connectivity is restored. An operator can take time to reach the cable and can acknowledge an action late. Neither the reconnect prompt nor its acknowledgement is an exact restoration timestamp. A successful TCP or API request provides an upper bound, but cannot establish when restoration first occurred.

`NetworkLinkObservation` supplies a separate, opt-in observation for an Ethernet-only processor interruption. Its temporary test package samples Linux `eth0` carrier state and the carrier-transition counter on the processor. It records the last confirmed down sample and the first confirmed up sample. The resulting interval describes physical carrier restoration, not API readiness. It cannot measure a processor power interruption, a device's independent link, or an upstream outage that leaves the processor's Ethernet carrier up.

The controller binds an exact processor, existing reservation owner, test-package hash, model and location. It prepares the temporary host before readiness and arms it only after the operator is ready. Waiting overnight for readiness does not consume the observation period. Arming is attempted once; an ambiguous result is preserved, never silently retried. The sampler survives the expected controller connection loss and stops after a complete cycle, failure, 30 minutes, or disposal of its test host.

Every sample is validated against the transition counter. Missing transitions, an initially disconnected interface, clock reversal, a sampling gap above two seconds, and invalid kernel observations prevent a timing pass. The maximum sampling interval and relevant raw samples remain in the retained trace. A connected-only diagnostic is never outage evidence.

The processor's monotonic ticks are mapped to a conservative controller UTC interval using authenticated request/response bounds before and after the interruption. The clocks are not assumed equal. The conversion allows 100 ppm drift plus 50 ms in either direction and rejects inconsistent clock anchors. The host's elapsed round-trip time must also agree with its UTC interval. A changed trace identity, processor/program epoch or wrong interface prevents reuse of the observation.

The DevTools manual recorder accepts this interval through `ISubmissionManualRestorationWindow`. It intersects the physical interval with the operator request and any independently observed restored-by bound. The raw proof is retained and hashed with the operator request and response. Required functional recovery measurements and the original deadline remain mandatory. There is no conversion of older incomplete results into passes.

Cleanup exports the trace, removes the exact temporary test instance, joins its sampler, removes only that run's verified package storage and retains cleanup receipts. The caller keeps its processor reservation until cleanup succeeds. Uncertain activation or cleanup is actionable and must be reconciled before another workflow. Cached catalogue metadata does not justify rebooting a processor.

When an installed-driver workflow already holds its control guard, recorder preparation and cleanup verify and retain that exact owner's guard. They never replace or release it. Another owner's guard or a changed marker prevents cleanup. The enclosing workflow remains responsible for releasing its guard and reservation after recorder cleanup has been confirmed.

Protocol 2 provides authenticated `link-arm`, `link-read` and `link-stop` requests using the unique trace ID in `TargetId`. Replies use the existing `complete`/`error` envelope. These commands only observe an interface; they cannot disconnect, enable or configure it. A trace cannot be replaced within the same temporary host.
