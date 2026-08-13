Feature: Authoring mistakes that are legal Gherkin

Every scenario below compiles and runs under Reqnroll without complaint. Each one is
also almost certainly not what its author meant. FeatureValidator exists to say so.

Scenario Outline: An unused Examples column
	Given I entered <a>
	Then the result should be <result>

	Examples:
		| a | b | result |
		| 1 | 9 | 1      |

Scenario Outline: A placeholder with no column
	Given I entered <a>
	Then the total is <nonexistent>

	Examples:
		| a |
		| 1 |

Scenario: A plain scenario using placeholders
	Given I entered <a>
	Then the result should be <a>

Scenario Outline: An outline that substitutes nothing
	Given I entered 1
	Then the result should be 1

	Examples:
		| irrelevant |
		| 1          |
		| 2          |

Scenario Outline: A column used only in a data table
	Given the following users
		| name   |
		| <user> |
	Then it works

	Examples:
		| user |
		| Ada  |

Scenario Outline: A column used only in a doc string
	Given the payload
		"""json
		{ "id": "<identifier>" }
		"""
	Then it works

	Examples:
		| identifier |
		| 42         |

Scenario Outline: A perfectly good outline
	Given I entered <a>
	And I entered <b>
	Then the result should be <result>

	Examples:
		| a | b | result |
		| 1 | 2 | 3      |

Scenario: A perfectly good scenario
	Given I entered 1
	Then the result should be 1
