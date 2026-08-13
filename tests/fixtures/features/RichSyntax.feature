@feature-level
Feature: Doc strings, data tables and comments

	A free-text description under the Feature header. The parser must not mistake this
	for a scenario, and a caret parked in it must resolve to the whole feature.

	Background:
		Given a clean slate

	# A comment directly above a tagged scenario.
	@slow @wip
	Scenario: A scenario with a data table
		Given the following users
			| name  | role  |
			| Ada   | admin |
			| Grace | dev   |
		Then there are 2 users

	Scenario: A scenario with a doc string
		Given the payload
			"""json
			{
			  "Scenario": "this line looks like a keyword but is inside a doc string",
			  "Feature": "so is this one"
			}
			"""
		Then it is parsed

	Rule: A rule with two scenarios

		Background:
			Given the rule-level background

		Scenario: First inside the rule
			Given something

		Scenario: Second inside the rule
			Given something else
