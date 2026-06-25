-- Adds ordered multi-instruction effect storage to cards, alongside the existing single
-- EffectType/EffectAmount columns. When EffectsJson is a non-empty array, the engine executes
-- those steps in order instead of the single legacy effect (e.g. "Deal 4 to a unit. Draw 1."
-- becomes [{"type":"damage","amount":4},{"type":"draw","amount":1}]).
ALTER TABLE cards ADD COLUMN IF NOT EXISTS "EffectsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
