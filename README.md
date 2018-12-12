# Features
* Changes time client sided depending on Zone Manager zone the client is in
* Uses the API from NightVision to change time depending on the zone you're in
* Uses the API from ZoneManager so uses the same ID's as Zone Manager (refer to Zone Manager documentation for help with ID's)

# Permissions
* timezones.admin - Required to use any of the commands
# Commands
[] = optional & () = required

**Chat/Console commands**
* timezone set (Zone ID) (day/night) - Adds a new timezone either day or night.
* timezone disable (Zone ID) - Disables the given timezone (Removing it)
* timezone toggle [player] - Toggles timezones on/off of for self or the given player
* timezone list - returns a list of available timezones.