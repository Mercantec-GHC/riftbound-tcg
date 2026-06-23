# Abilities

Source: Riftbound Core Rules.pdf, last updated 2026-03-30.

These notes preserve the numbered rule structure from the source PDF while normalizing spacing and PDF extraction artifacts.

### 360. Abilities

**361.** An Ability is the structured rules and capabilities of Game Objects or Spells.

**361.1.** An Ability has multiple structures. Passive Abilities Replacement Effects Activated Abilities Triggered Abilities Delayed Abilities

**362.** A card can have more than one Ability and more than one type of Ability.

### 363. Passive Abilities

**364.** Conditions, rules, constraints, or statements that affect the course of regular play.

**364.1.** These abilities have a wide variety of formats to recognize. Example: "I get +1 [M] while you have 2 or more cards in your hand." Example: "Friendly Yordles at my battlefield have [Shield]."

**364.2.** They can be recognized by being statements of fact.

**364.3.** Passive Abilities can be conditional.

**364.3.a.** Conditional Passive Abilities can be recognized by the occurrence of "if" or "while" as part of the statement of the ability. Example: "While I'm attacking or defending alone, I have +2 [M]." Example: "If an opponent controls a battlefield, I enter ready. "### 365. Presence on Permanents

**365.1.** Passive Abilities of Permanents are typically only active while on the Board.

### 366. Presence on Card outside of the Board

**366.1.** Passive Abilities of cards in zones that are outside of the Board will self-describe their context. Example: The passive ability "Play me only during an opponent's turn." applies in any zone from which that card can be played.

**366.2.** Passive Abilities can alter the costs of cards as they are played.

**366.2.a.** These apply at all times in any zone from which the card with the ability can be played.

### 367. Replacement Effects

**368.** An ability that alters the application of another game effect or game rule.

**368.1.** Passive Abilities can be Replacement Effects.

**369.** Replacement Effects intercede during the execution of a Game Effect and alter its execution.

**369.1.** A Replacement Effect can usually be identified by the presence of the terms "would" or "instead." Example: Zhonya's Hourglass reads "The next time a friendly unit would die, kill this instead. Heal that unit, exhaust it, and recall it." This is a replacement effect that alters the execution of any Game Effect that would kill a friendly unit.

**369.2.** Some Game Actions are themselves Replacement Effects. Example: Burning Out is a replacement effect. Example: Preventing Damage is a replacement effect.

**370.** A Replacement Effect can alter the typical flow of play, including other cards' executions.

**370.1.** Replacement Effects apply to any event or instruction that qualifies for their application. A Replacement Effect will specify the circumstances by which an event or instruction will qualify to be replaced.

**370.1.a.** When a Replacement Effect applies, it replaces the qualifying event with one or more Game Actions or events, or the qualifying instruction with another instruction.

**370.2.** A Replacement Effect can only be applied once to an event, or to any Game Actions or events that replace that event. Example: A player plays a spell that reads "gear you control become 1 [M] gear units this turn." They control two copies of Zhonya's Hourglass when the spell resolves. If one of those copies is killed, both of their Replacement Effects will be applied. Whichever is applied first, that Replacement Effect can't be applied again. When it is applied, it kills its source, which creates an event the other can apply its Replacement Effect to. Once they've both applied their Replacement Effect to the original death event and the event that replaced it, they cannot go any further. At that point, whichever Zhonya's Hourglass applied its Replacement Effect last will die.

**370.3.** If a Game Object has a Replacement Effect that is active in a specific zone, it is evaluated and subsequently applied if it enters that zone before an event occurs that it could replace. Example: A unit that reads "if a unit you control would die, you may banish me from your trash instead. If you do, heal that unit, exhaust it and recall it." The first unit dies simultaneously with a 1 [M] Recruit token. It does not enter the trash before the Recruit dies, so it will not be able to replace its death.

**371.** Some Replacement Effects will begin with "once each turn."

**371.1.** When an event the Replacement Effect could apply to occurs, the player who controls the Replacement Effect may choose to apply it to the event.

**371.2.** If they do not, it has not been applied this turn.

**372.** If more than one Replacement Effect applies to the same event being executed, then the owner of the object being acted on determines the order the Replacement Effects will apply.

**372.1.** If it is a player being acted on, that player decides the order the Replacement Effects will apply.

**372.2.** If the affected object is an Uncontrolled Battlefield then the Current Turn Player decides the order the Replacement Effects will apply.

**373.** If more than one event occurs simultaneously that Replacement Effects could apply to, each event is treated separately and individually for the purposes of Replacement Effects, and Replacement Effects with the same controller are applied in the order of their controller's choosing. Example: Two units controlled by the same player die in the same cleanup. That player also controls Zhonya's Hourglass. They must decide which event to apply Zhonya's Hourglass to first.

**373.1.** Although these events are simultaneous, the applied Replacement Effects are ordered. If multiple applied Replacement Effects with different controllers would execute simultaneously, they execute in turn order.

**373.2.** When applying Replacement Effects to events that occur simultaneously, each Replacement Effect may only be applied in one sequence, to any number of events that are qualified to be replaced. Example: Soraka, Wanderer reads "If another unit you control here would die, if it has less Might than me, instead heal it, exhaust it, and recall it." Soraka dies simultaneously with two 1 [M] Recruit tokens at the same battlefield and two 1 [M] Recruit tokens in base. Soraka has a Guardian Angel attached to her when she dies, which appends "If I would die, kill Guardian Angel instead. Heal me, exhaust me, and recall me" to Soraka's rules text. There are several possible ways to order the Replacement Effects being applied to the various events: If Soraka's Replacement Effect is applied first, it saves the Recruits at the same battlefield as her but not the Recruits in base. If the Replacement Effect appended by Guardian Angel then saves Soraka, she cannot apply her Replacement Effect to the Recruits in base as her Replacement Effect has already been applied to an event simultaneous with it dying. If the Replacement Effect appended by Guardian Angel is applied first, it saves Soraka and recalls her - then when Soraka's Replacement Effect is applied, it can only save the Recruits in base.

**373.2.a.** A sequence of Replacement Effects is an uninterrupted series of applications to a set of simultaneous events.

**373.2.a.1.** A Replacement Effect that replaces an event or Game Action that is part of another Replacement Effect will not interrupt the sequence of the replaced Replacement Effect's application.

**374.** A Replacement Effect 's controller is the player that controls the source of the Replacement Effect.

**375.** If an event that a Replacement Effect applies to would be modified by a Game Effect, or the results of that event would be modified by a Game Action, the Replacement Effect will inherit those modifications. Example: Treasure Hunter reads "When I move, play a Gold gear token exhausted." A Replacement Effect that says "if you would play a token gear, play that token and an additional copy instead" is applied to the event of the Gold gear token being played. The additional copy will also be exhausted, as it inherits the "exhausted" modification. Example: Another Replacement Effect says "if you would play a token, draw 1 instead." The modification cannot apply, so we ignore it. Example: A spell reads "play a ready 3 [M] Mech token. Then do this: Give it Temporary." A Replacement Effect that says "if you would play a unit token, play that token and a 1 [M] Recruit token instead" is applied to the event of the Mech token being made. The Recruit token enters ready and is given Temporary.

### 376. Activated Abilities

**377.** Activated Abilities are repeatable effects with a cost. They follow a process of going onto the chain and resolving, similar to Playing a Card. See rule 349. Playing Cards for more information.

**377.1.** Activated Abilities are recognized by the presence of a ":" in the text of the card, preceded by a cost and succeeded by an effect. Example: "[2]: Draw 1" is an activated ability. The cost is 2 energy. The effect is to draw 1 card.

**377.2.** Card text will refer to activating Activated Abilities with the word "use."

**377.2.a.** If "using" an Activated Ability is part of a trigger condition, that condition is fulfilled when the Activated Ability resolves.

**377.2.b.** If an Activated Ability has a condition on "using" it, that condition must be true in order to activate the ability in question. Example: Ultrasoft Poro reads "[E]: Play two 1 [M] Bird unit tokens with [Deflect]. Use this ability only while I'm at a battlefield." In order to activate the ability, Ultrasoft Poro must be located at a battlefield.

**377.3.** Activated Abilities use the chain.

**377.3.a.** Declare activation of the Ability.

**377.3.a.1.** The ability goes on the chain but has no card to represent it, so players need to take note that it is now a Closed State.

**377.3.b.** Proceed with executing the Chain.

**377.3.b.1.** Follow the steps of "Playing or Activating Abilities" in rule 398. This ability will become a Pending Chain Item.

**377.3.b.2.** Opponents have an opportunity to respond, as appropriate, as if a card was played onto the chain.

**377.3.b.3.** If no further action is taken, execute the Activated Ability.

**378.** The controlling player chooses when and whether to activate an Activated Ability.

**379.** Activated abilities are present on Game Objects and some Spells.

**380.** Can primarily be activated while on the Board.

**381.** All Activated Abilities can only be activated on the Controlling Player's Turn and during an Open State.

### 382. Triggered Abilities

**383.** Triggered Abilities are repeatable effects that happen when a Condition is met.

**383.1.** Triggered Abilities can usually be recognized by the word "when" followed by a game action or event; the word "at" followed by a point in time during the turn sequence; or the phrase "the [Nth] time" followed by a game action or event. Examples: "When you conquer here, you may spend a buff to draw 1." "At the end of your turn, ready 2 runes." "The first time I move each turn, you may ready something else that's exhausted."

**383.1.a.** The phrases that identify triggered abilities do not always appear at the beginning of sentences or abilities.

**383.1.b.** If an ability triggers "the [Nth] time" something happens and that trigger condition is met multiple times simultaneously, the ability's controller picks one of those instances to serve as the trigger condition. The ability triggers only once, due to the chosen condition. Example: Wraith of Echoes reads "The first time another friendly unit dies each turn, draw 1." That ability hasn't triggered yet this turn. Two other friendly units die simultaneously (say, due to combat damage). The Wraith's controller chooses one of those deaths to trigger Wraith's ability.

**383.2.** Triggered Abilities have a Condition and an Effect.

**383.2.a.** The Condition follows the When.

**383.2.b.** The Effect is the Instruction that follows the comma after the Condition.

**383.2.c.** The Condition of a Trigger is evaluated after a potentially inciting event has been processed.

**383.2.c.1.** If a Game Object with a Triggered Ability that is active in a specific zone, it is evaluated and subsequently triggered if it enters that zone at the same time that its Trigger 's condition is met. Example: Immortal Phoenix says "When you kill a unit with a spell, you may pay [1][C] to play me from your trash." This ability triggers if Immortal Phoenix is in your trash immediately after you kill a unit with a spell, even if the unit you killed with a spell was that Immortal Phoenix.

**383.2.c.2.** A Game Object will not be able to successfully be able to evaluate its Trigger Condition, however, if it leaves the zone that its Trigger is active from at the same time that its Trigger is satisfied. Example: Viktor, Leader says "When another non-Recruit unit you control dies, play a 1 [M] Recruit unit token into your base." This ability triggers if Viktor is on the board immediately after another non-Recruit unit you control dies. It does not trigger if Viktor and another non-Recruit unit you control die during the same game action (for instance, if they are both killed in the same Cleanup due to the damage dealt by Unchecked Power).

**383.3.** When a Condition is met, a Triggered Ability behaves like an Activated Ability and is placed on the Chain.

**383.3.a.** If a Triggered Ability says "you may" as the first part of its Effect, the controller of its source will choose whether or not to place the Triggered Ability on the chain when its trigger Condition is fulfilled.

**383.3.b.** If a Triggered Ability contains a cost within instructions, that cost is treated as the base cost of the Triggered Ability.

**383.3.b.1.** The cost must be paid in order to finalize the Triggered Ability to the Chain.

**383.3.b.2.** Costs within instructions for Triggered Abilities are not paid on resolution, unlike costs within instructions for Spells.

**383.3.c.** Triggered Abilities can be put on the Chain during Closed States or Open States on any player's turn.

**383.3.d.** If more than one Triggered Ability is Triggered simultaneously, then the player that controls the Abilities selects the order to place them on the Chain.

**383.3.d.1.** If multiple players separately control Triggered Abilities that are Triggered simultaneously, then starting with the Turn Player and proceeding in Turn Order, each player orders their Triggered Abilities on the Chain.

**383.3.e.** If a triggered ability has a conditional statement following the Condition, that conditional statement must be true in order for the trigger to be placed on the Chain. Example: Sona, Harmonious reads "At the end of your turn, if I'm at a battlefield, ready up to 4 friendly runes." Her Trigger Ability's Condition will be fulfilled in the Ending Step, but the Triggered Ability will only be placed on the chain if she is located at a battlefield when the Condition is fulfilled.

**383.3.f.** Some Triggered Abilities will trigger "once each turn."

**383.3.f.1.** When the Condition is fulfilled, the player who controls the source of the Triggered Ability may choose to place it on the chain.

**383.3.f.2.** If they do not, it has not Triggered this turn.

**383.4.** Some Conditions are commonly used and structured in a way that explicitly defines their use and other properties of the Effect that is associated with it.

**383.4.a.** Play Effects are Triggered Abilities with the Condition that the Permanent that has the Play Effect being played to the board.

**383.4.a.1.** These are commonly structured as "When you play me..." for Units and "When you play this..." for Gear.

**383.4.a.2.** These Triggered Abilities are put on the Chain as Pending Items after the Permanent these effects correspond to is finalized and enters the board.

**383.4.a.3.** These Triggered Abilities can be referred to as Play Effects.

**383.4.a.4.** Abilities that trigger when another object is played are not considered Play Effects.

**383.4.b.** Targeting Effects are Triggered Abilities with the Condition that a Game Object becomes targeted.

**383.4.b.1.** These are commonly structured as "When you choose me..." or "When you choose a [Game Object]..."

**383.4.b.2.** These Triggered Abilities are put on the Chain as Pending Items after a spell or ability that targets an appropriate Game Object is Finalized.

**383.4.b.3.** Although these abilities say "choose" in their Condition, they trigger specifically when an appropriate Game Object is Targeted. See rule 355.6. Targeting for more information on what counts as Targeting.

**383.4.b.4.** These Triggered Abilities can be referred to as Targeting Effects.

**383.4.c.** Conquer Effects are Triggered Abilities with a Condition of a Unit participating in, and successfully Conquering a Battlefield.

**383.4.c.1.** These are commonly structured as "When I conquer..." and "When you conquer..."

**383.4.c.2.** This category of Triggered Abilities encompasses only those that are triggered from Units that were present during the Conquer action, or Abilities that reference the player that performed the Conquer action.

**383.4.c.2.a.** The Conquer Abilities of Units are put on the Chain as Pending Items after the Unit(s) these effects correspond to are present at a Battlefield when a player gains control of it and gains 1 Victory Point from Conquering.

**383.4.c.2.b.** The Conquer Abilities of anything that references the player Conquering is put on the Chain as a Pending Item when the Condition that the player that controls the triggering source has performed a Conquer and gained 1 Victory Point.

**383.4.c.2.c.** If the act of gaining one point from Conquering is negated or replaced in any way, the Conquer Effect will still trigger.

**383.4.c.3.** These Triggered Abilities can be referred to as Conquer Effects.

**383.4.d.** Hold Effects are Triggered Abilities with a Condition of a Unit being present at a Battlefield during the Beginning phase when a player scores Victory Points from Holding.

**383.4.d.1.** These are commonly structured as "When I hold..." or "When you hold..."

**383.4.d.2.** This category of Triggered Abilities encompasses only those that are triggered from Units that were present during the Hold action, or Abilities that reference the player that performed the Hold action.

**383.4.d.2.a.** The Hold Abilities of Units are put on the Chain as Pending Items after the Unit these effects correspond to are present at a Battlefield when a player maintains control of it and Gains 1 Victory Point during their Beginning Phase from Holding.

**383.4.d.2.b.** The Hold Abilities of anything that references the player Holding is put on the Chain as a Pending Item when the Condition that the player that controls the triggering source has performed a Hold and gained 1 Victory Point.

**383.4.d.2.c.** If the act of gaining one point from Holding is negated or replaced in any way, the Hold Effect will still trigger.

**383.4.d.3.** These Triggered Abilities can be referred to as Hold Effects.

**383.4.e.** Attack Triggers are Triggered Abilities that trigger when a Unit gains the Attacker designation for the first time during a combat.

**383.4.e.1.** These are commonly structured as "When I attack..."

**383.4.e.2.** These Triggered Abilities are put on the Chain as Pending Items after the Unit these effects correspond to gains the Attacker designation during Combat.

**383.4.e.2.a.** These triggers will only have their condition checked once per combat, despite a Unit being able to gain and lose the Attacker designation multiple times in the same combat.

**383.4.e.2.b.** If the trigger condition contains other requirements besides attacking and if those requirements are not fulfilled when the unit gains the Attacker designation, it will not trigger in that combat.

**383.4.e.3.** These Triggered Abilities can be referred to as Attack Triggers.

**383.4.f.** Defend Triggers are Triggered Abilities that trigger when a Unit gains the Defender designation for the first time during a combat.

**383.4.f.1.** These are commonly structured as "When I defend..."

**383.4.f.2.** These Triggered Abilities are put on the Chain as Pending Items after the Unit these effects correspond to gains the Defender designation during Combat.

**383.4.f.2.a.** These triggers will only have their condition checked once per combat, despite a Unit being able to gain and lose the Defender designation multiple times in the same combat.

**383.4.f.2.b.** If the trigger condition contains other requirements besides defending and if those requirements are not fulfilled when the unit gains the Defender designation, it will not trigger in that combat.

**383.4.f.3.** These Triggered Abilities can be referred to as Defend Triggers.

### 384. Presence on Permanents

**384.1.** Typically active while on the Board.

**384.2.** Triggered Abilities of Permanents are only able to have their Conditions evaluated while on the Board.

### 385. Presence on Cards outside of the Board

**385.1.** Triggered Abilities on cards outside of the Board rely on the Information Level of the zone they are in.

**385.2.** Triggered Abilities outside of the Board will self-describe their context. Example: The triggered ability "When you conquer, you may discard 1 to return this from your trash to your hand." triggers while the card it's on is in the trash, and not anywhere else.

**386.** Reflexive Triggers

**387.** Reflexive Triggers are a type of Triggered Ability that create one or more Chain Items when their condition is met.

**387.1.** Reflexive Triggers can be recognized by the phrase "Do this: "or "Do one of the following: ".

**387.1.a.** "Do this" can be followed by "N times." The Reflexive Trigger will thus be added to the chain N times when its condition is met.

**387.2.** Reflexive Triggers will be preceded by their conditions, if any. If no condition is present in the ability then the Reflexive Trigger will always be added to the Chain.

**387.3.** If present, the Condition of a Reflexive Trigger will follow the same format as a Triggered Ability.

**388.** Reflexive Triggers use the Chain.

**388.1.** A new ability is created and added to the chain as a Pending Item. See rule 398. Playing or Activating Abilities for more information.

**388.2.** If a Reflexive Trigger creates more than one Pending Item it creates them all in order, but does not go beyond the first step of adding them to the Chain. See rule 398. Playing or Activating Abilities for more information.

### 389. Delayed Abilities

**390.** Delayed Abilities are a type of Ability whose trigger Condition identifies a specific time during a turn or a specific event that can occur during a specific timeframe.

**390.1.** Delayed Abilities can be any other type of Ability, and contain all of the properties of that type in addition to the properties of Delayed Abilities.

**390.2.** Delayed Triggers are Triggered Abilities that can be recognized by describing a specific time of the turn, or by structuring a Triggered Ability with a specific frame of time as a restriction.

**390.3.** Delayed Replacements are Replacement Effects that can be recognized by specifying the effect they are replacing at a specific time, or "the [Nth] time" in the description of the effect as it resolves.

**390.4.** Delayed Passive Abilities are Passive Abilities that are applicable only during a specified window of time. The time that the Delayed Passive Ability applies will be recognized in the effect that initiates it.

**390.5.** Delayed Linked Abilities are Linked Abilities that are generated by another Ability and reference that Ability or Game Objects it affects. Delayed Linked Abilities don't necessarily identify a specific window of time, and instead are defined by the Ability they are linked with.

**390.5.a.** If that Ability affects a Game Object, the Delayed Linked Ability's window will be as long as that Game Object is in an appropriate zone.

**390.5.b.** If the Delayed Linked Ability references the source of the abilities, its window will be as long as the source is in an appropriate zone. Refl391. Delayed Abilities will resolve or be active just like the ability they augment, but only during the specified time in the effect that created the Delayed Ability. Example: Ravenborn Tome reads "The next spell you play this turn deals 1 Bonus Damage" is a Delayed Passive Ability that passively adds 1 damage to just the next spell played. The next spell is a specific time, and the 1 Bonus Damage is a passive ability. Example: Noxian Guillotine reads "Choose a unit. Kill it the next time it takes damage this turn." When the chosen unit takes damage is the specified time, and killing it is the condition for a Delayed Triggered Ability.

**392.** Delayed Abilities are not associated with Units or Gear; they are created by other Abilities or Spells. As such they are executed when their condition and/or specified time occurs regardless of whether the source of the Delayed Ability is still on the board or not.

### 393. Linked Abilities

**394.** Linked Abilities are a set of Abilities with one or more of the component Abilities referencing the other Abilities in the set.

**395.** In order for a set of Abilities to be Linked, they must be present in the printed Effect or Rules Text of the same Game Object, or be granted by the same source to another Game Object.

**396.** Linked Abilities can contain component Abilities of any type.

**397.** A component Linked Ability that references a Game Object affected by another Ability in the set may only interact with Game Objects affected by the Abilities it is Linked with.

### 398. Playing or Activating Abilities

**399.** Playing or activating Abilities follows the same steps of playing cards.

**400.** Abilities when added to the Chain become Pending Items until they complete the steps of Playing.

**400.1.** When an Ability finishes the steps of playing it becomes a Chain Item just like a Spell.

**400.2.** When an Ability with the [Add] action is finalized it resolves immediately instead of becoming a Chain Item, like a Unit or Gear.

**401.** 1. Activate or trigger the Ability

**401.1.** Add a Pending Item to the chain representing the Ability that is either being Activated or Triggered. Notably, although this Chain Item will not have a card representing it, this will create a Closed State. See rule 349. Playing Cards for more information.

**401.2.** If there is currently a game effect being resolved, continue resolving the game effect instead of continuing the following steps.

**402.** 2. Make relevant choices

**402.1.** Make all choices required for this ability, such as targets, modes, or other relevant decisions. See rule 349. Playing Cards for more information

**402.2.** If legal options are not available for an Activated Ability, it is not legal to activate it.

**402.3.** If there are not enough options to make legal choices for a Triggered Ability that has been put on the chain, remove it from the Chain now. It ceases to be a Pending Item but never becomes a Chain Item.

**402.3.a.** This is not an Ability being countered.

**402.3.b.** If there are legal options to choose, the ability's controller must choose them. They may not decline this stage of playing a Trigger.

**403.** 3. Determine Total Cost

**403.1.** Determine the base cost of the Ability.

**403.1.a.** Activated Abilities will have a cost listed before the ":" in their text.

**403.1.b.** Triggered Abilities will typically not have a base cost associated with them when placed on the chain due to their conditions.

**403.1.b.1.** If a Triggered Ability has a cost within instructions (e.g. "[do X] to [do Y]"), the cost is taken as the base cost. See rule 742.1. for more information on costs within instructions.

**403.2.** Apply cost increases and decreases as a result of choices made in the prior step. See rule 349. Playing Cards for more information.

**403.3.** Apply any other cost increases or decreases as necessary. See rule 349. Playing Cards for more information.

**404.** 4. Pay Costs

**404.1.** Pay costs as determined in the prior step. See rule 349. Playing Cards for more information.

**404.2.** At this stage, players may decline to pay for Triggered Abilities that have incurred a cost. If they do, the ability will cease being a Pending Item and be removed from the Chain. It never becomes a Chain Item.

**404.2.a.** This is not an Ability being countered.

**405.** 5. Check Legality

**405.1.** Ensure that the Ability's targets are still legal.

**405.2.** Ensure that the Ability's effect would not create an illegal state. If it would, resolve in the same way you would resolve a Card that creates an illegal state. See rule 349. Playing Cards for more information.

**406.** 6. Proceed with Play

**406.1.** This Ability is no longer Pending.

**406.2.** This Ability becomes a Chain Item.

**406.3.** If there are other Pending Items on the Chain, their controllers perform the remaining steps of playing now.

**406.4.** Other players have an opportunity to play Reactions before the resolution of spells. See rule 327. Chains for more information.

**406.5.** Otherwise, execute the Ability just like a Spell, then clear the Chain Item from the Chain.
