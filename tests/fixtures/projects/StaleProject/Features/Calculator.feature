Feature: Calculator (basic) & more

Every scenario below exists to exercise a specific mapping edge case, so please do not
"tidy" the titles — the punctuation, unicode and duplicate-looking names are the point.

Background:
	Given the calculator is on

@smoke
Scenario: Add two numbers
	Given I entered 50
	And I entered 70
	When I press add
	Then the result should be 120

Scenario: Multiply, two numbers
	Given I entered 6
	And I entered 7
	When I press multiply
	Then the result should be 42

Scenario: Ivan's "quoted" (tricky) & odd | title ~ = !
	Given I entered 1
	And I entered 1
	When I press add
	Then the result should be 2

Scenario: Ünïcödé — スカラー
	Given I entered 2
	And I entered 3
	When I press add
	Then the result should be 5

Scenario Outline: Add many <a> and <b>
	Given I entered <a>
	And I entered <b>
	When I press add
	Then the result should be <result>

	Examples:
		| a | b | result |
		| 1 | 2 | 3      |
		| 4 | 5 | 9      |

	@extra
	Examples: second block
		| a  | b  | result |
		| 10 | 20 | 30     |

Rule: Subtraction has its own rule block

	Scenario: Subtract inside a rule
		Given I entered 9
		And I entered 4
		When I press subtract
		Then the result should be 5

Scenario: Added after the last build
	Given I entered 1
	When I press add
	Then the result should be 1
