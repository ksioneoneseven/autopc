namespace AutoPilotAgent.OpenAI;

internal static class Schemas
{
    internal const string PlanSchema = /*lang=json*/ """
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "goal": { "type": "string" },
    "clarifying_questions": {
      "type": "array",
      "items": { "type": "string" }
    },
    "required_apps": {
      "type": "array",
      "items": { "type": "string" }
    },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "id": { "type": "integer" },
          "description": { "type": "string" },
          "risk_level": { "type": "string", "enum": ["low","medium","high"] },
          "requires_confirmation": { "type": "boolean" },
          "validation": { "type": "string" }
        },
        "required": ["id","description","risk_level","requires_confirmation","validation"]
      }
    }
  },
  "required": ["goal","clarifying_questions","required_apps","steps"]
}
""";

    internal const string ActionSchema = /*lang=json*/ """
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "action_type": {
      "type": "string",
      "enum": [
        "focus_window",
        "click_coordinates",
        "click_uia",
        "type_text",
        "hotkey",
        "wait",
        "verify"
      ]
    },
    "parameters": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "title": { "type": "string" },
        "process": { "type": "string" },
        "x": { "type": "integer" },
        "y": { "type": "integer" },
        "automation_id": { "type": "string" },
        "name": { "type": "string" },
        "text": { "type": "string" },
        "uia_automation_id": { "type": "string" },
        "uia_name": { "type": "string" },
        "keys": {
          "type": "array",
          "items": { "type": "string" }
        },
        "ms": { "type": "integer" }
      },
      "required": [
        "title",
        "process",
        "x",
        "y",
        "automation_id",
        "name",
        "text",
        "uia_automation_id",
        "uia_name",
        "keys",
        "ms"
      ]
    },
    "requires_confirmation": { "type": "boolean" },
    "expected_result": { "type": "string" }
  },
  "required": [
    "action_type",
    "parameters",
    "requires_confirmation",
    "expected_result"
  ]
}
""";
}
