---
name: wifi-no-internet
description: Diagnose a Wi-Fi or Ethernet connection that shows connected but has no internet. Use for wifi, internet, network, no connection, pages do not load.
metadata:
  tools: "net_get_config net_ping net_dns_lookup settings_open"
---
# When to use
The network shows "connected" but web pages do not open.

# Steps
1. Run net_get_config. Find the adapter with a default gateway.
2. If no adapter has an IP address or a gateway, tell the user to reconnect, and run settings_open {"page": "network-wifi"}.
3. Run net_ping to the gateway. If it fails, the problem is the local network.
4. Run net_ping to 1.1.1.1. If it fails, the problem is the router or the provider.
5. Run net_dns_lookup for "www.microsoft.com". If it fails but step 4 passed, the problem is DNS.
6. Give the result and one fix for each failed check.

# Example
User: my wifi is connected but nothing loads
Step: {"thought":"Check adapter first","action":"net_get_config","args":{}}
