# USB Refresh Manual Test Plan

## Scope
Validate centralized USB refresh coordination in Guest app:
- No parallel refresh execution
- Burst requests are merged
- Debounce/Throttle for automatic/background reasons
- Event subscription lifecycle for main window closing hook
- Traceability for blocked/skipped/merged/running refreshes

## Preconditions
- Build succeeds for Guest project.
- Host/Guest communication available for USB listing.
- Logging enabled in Guest config for debug traces.

## Test Cases

1. Manual refresh always allowed when background communication is disabled
- Set Guest config: usb.backgroundCommunicationEnabled=false.
- Trigger manual refresh from UI and tray.
- Expect refresh execution and log entries with reason=Manual.
- Verify no blocked log for manual reason.

- Background refresh blocked when background communication is disabled (startup bootstrap remains allowed)
- Keep usb.backgroundCommunicationEnabled=false.
- Restart Guest and wait for startup/background paths.
- Expect logs:
  - startup-initial-usb-list is executed once as bootstrap load
  - usb.refresh.blocked.background_disabled appears for reason=Background
- Verify periodic background refreshes remain blocked while startup bootstrap still loads catalog once.

3. Connect/Disconnect/PushNotification still allowed when background communication is disabled
- With usb.backgroundCommunicationEnabled=false, trigger:
  - connect action
  - disconnect action
  - push notification (share changed)
- Expect refresh requests accepted and begin/success or failed trace logs.
- Ensure none are blocked by background policy.

4. Burst merge and replay
- Trigger many refresh requests quickly (push + manual + connect).
- Expect:
  - one active refresh at a time
  - usb.refresh.merged_pending logs during burst
  - usb.refresh.pending_replay when merged request executes next
- Verify final list reflects latest high-priority action.

5. Debounce for automatic and push requests
- Trigger repeated push notifications quickly.
- Expect usb.refresh.delayed logs with small delay.
- Verify fewer actual refresh begin events than requests.

6. Background throttle minimum interval
- Trigger repeated background requests in short interval.
- Expect usb.refresh.skipped.background_throttled entries.
- Verify refresh begin frequency is reduced.

7. Closing handler lifecycle on theme window reopen
- Trigger theme change that recreates window.
- Expect logs:
  - ui.window.closing_handler.registered for new window
  - ui.window.closing_handler.unregistered when closing old window
- Verify closing the app afterward triggers exit flow once.

8. Session ending hook lifecycle
- Start app and close it cleanly.
- Expect register and unregister traces:
  - ui.session_ending_hook.registered
  - ui.session_ending_hook.unregistered

## Log Matrix (Reason/Trigger expected behavior)

- Manual:
  - Typical triggers: manual-refresh-default, manual-refresh, tray-window-refresh-button, tray-control-center-open, theme-reopen-sync
  - Expected when backgroundCommunicationEnabled=false: allowed

- Connect:
  - Typical triggers: connect-after-share-request, connect-already-exported-recheck, tray-connect-refresh
  - Expected when backgroundCommunicationEnabled=false: allowed

- Disconnect:
  - Typical triggers: tray-disconnect-refresh, transport-switch-pre-scan, transport-switch-post-scan
  - Expected when backgroundCommunicationEnabled=false: allowed

- PushNotification:
  - Typical triggers: push-share-changed-aggressive, push-share-changed-lightweight
  - Expected when backgroundCommunicationEnabled=false: allowed

- Background:
  - Typical triggers: background-periodic-refresh, push-followup-refresh, usb-transient-gap-recheck
  - Expected when backgroundCommunicationEnabled=false: blocked

- Startup bootstrap:
  - Typical trigger: startup-initial-usb-list
  - Expected when backgroundCommunicationEnabled=false: allowed (single initial load)
