Feature: Doc strings full of angle brackets

Gherkin's placeholder syntax is <name>, and that is also ordinary XML and HTML. A doc string is
exactly where markup gets pasted, so the two collide there and nowhere else. Reqnroll really does
substitute inside doc strings, so they cannot simply be ignored — but a tag is not a placeholder
just because it is bracketed.

Scenario Outline: An outline whose doc string contains XML
	Given I entered <a>
	And the response body
		"""xml
		<order><id>7</id><status>shipped</status></order>
		"""
	Then it works

	Examples:
		| a |
		| 1 |

Scenario: A plain scenario whose doc string contains XML
	Given the response body
		"""xml
		<order><id>7</id></order>
		"""
	Then it works

Scenario Outline: A column used only in a doc string, alongside XML
	Given the response body
		"""xml
		<order><id><identifier></id></order>
		"""
	Then it works

	Examples:
		| identifier |
		| 42         |

Scenario Outline: A genuinely undefined placeholder in step text
	Given I entered <a>
	And the total is <nonexistent>
	And the response body
		"""xml
		<order><id>7</id></order>
		"""
	Then it works

	Examples:
		| a |
		| 1 |
