USB Point Monitor - Stable v2

What changed from the broken build:
- The monitor no longer captures with tshark -D or Wireshark extcap.
- It captures directly with USBPcapCMD.exe using USBPcap slots (USBPcap1..USBPcap16).
- No etwdump/ETW selection is possible.
- No popup-per-log behavior. All logs stay in one UI text box.
- The installer no longer copies USBPcapCMD.exe into every extcap folder.
- The repair step dedupes old extcap copies and keeps one personal copy only.
- Every CMD pause is single-key R. No Enter.

Recommended flow:
1. Run 1_INSTALL_DEPS_ONCE.cmd once.
2. Reboot Windows if USBPcap was newly installed or if no USBPcap slots stay alive.
3. Run 2_CHECK_DEPS_ONLY.cmd and paste the log if it looks wrong.
4. Run 4_BUILD_EXE.cmd once.
5. Run 3_RUN_MONITOR.cmd.
6. In the UI click Start Capture.
7. Unplug/replug the keyboard dongle.
8. Type simple known keys in the test pad: A, B, Shift, Ctrl, Caps.
9. Let the keyboard/backlight sleep.
10. Toggle Caps/Num/Scroll from another keyboard or on-screen keyboard.
11. Click Stop + Analyze.
12. Click Copy Log and paste it into ChatGPT.

Notes:
- Capture may start many USBPcap slots. Wrong slots exit quickly; real USB root hubs stay alive.
- If no slots stay alive, reboot after USBPcap install.
- The Npcap warning from tshark -D is no longer a blocker for this monitor because capture is direct USBPcapCMD.
- Do not type passwords or private text while capture is running.
