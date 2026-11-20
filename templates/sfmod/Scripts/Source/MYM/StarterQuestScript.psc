Scriptname MYM:StarterQuestScript extends Quest
{MyMod's starter quest script. It starts with the game and writes one line to the Papyrus log.
Log lines start with "[MyMod]". To also see them on screen (PC only):
Settings > Gameplay Options > MyMod > Show Debug Messages (PC only).}

GameplayOption Property MYM_Option_Debug Auto Const Mandatory

Event OnQuestInit()
	Log("started")
EndEvent

; Every line goes to the Papyrus log (when Papyrus logging is on). With the debug option on, it also shows on
; screen. The Debug script does nothing on Xbox and PlayStation: text the player must see belongs in a Message.
Function Log(String asText)
	Debug.Trace("[MyMod] " + asText)
	If MYM_Option_Debug.GetValue() != 0.0
		Debug.Notification("[MyMod] " + asText)
	EndIf
EndFunction
