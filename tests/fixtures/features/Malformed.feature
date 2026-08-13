Feature: A feature the Gherkin parser rejects

Scenario: This one is fine
	Given something

Feature: A second Feature header in the same file, which Gherkin does not allow

Scenario: Never reached
	Given something
