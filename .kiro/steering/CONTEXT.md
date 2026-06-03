# AI_AGENT_CONTEXT.md

# READ THIS FIRST

This repository contains architecture, domain, and implementation rules spread across multiple files.

Before generating, modifying, reviewing, or refactoring any code, read the following files in order:

## 1. Architecture Rules (MANDATORY)

File:

.amazonq/context/knowledge/arch-rules.md

Purpose:

* Repository-wide development rules
* Layer responsibilities
* Dependency injection rules
* CQRS conventions
* Entity rules
* EF Core rules
* Authentication rules
* Naming conventions
* Project structure

This file defines WHAT is allowed and WHAT is forbidden.

Always treat this file as the source of truth.

---

## 2. Architecture Documentation (MANDATORY)

File:

.amazonq/context/knowledge/ARCHITECTURE_DOCUMENTATION.md

Purpose:

* Clean Architecture design
* DDD implementation
* CQRS flow
* MediatR pipeline
* Repository pattern
* UnitOfWork pattern
* Result pattern

Read this file to understand WHY the architecture exists.

---

## 3. Patterns Reference (MANDATORY)

File:

.amazonq/rules/patterns.md

Purpose:

* Existing implementation patterns
* Repository examples
* Query examples
* Command examples
* Validator examples
* API client patterns
* Pagination patterns
* Controller patterns

When generating code:

Follow the existing patterns instead of inventing new ones.

Consistency is preferred over creativity.

---

## 4. Business Domain Documentation (MANDATORY)

File:

.amazonq/context/knowledge/EEMO_Complete_Documentation.md

Purpose:

* Business rules
* Revenue collection workflows
* Facility definitions
* Delinquency rules
* Fee calculations
* Report requirements
* User roles
* Collection processes

This file is the source of truth for business logic.

Never guess business behavior.

---

## File Priority

When conflicts exist:

1. arch-rules.md
2. patterns.md
3. ARCHITECTURE_DOCUMENTATION.md
4. EEMO_Complete_Documentation.md

Follow this priority order.

---

## When Reviewing Existing Code

Check for:

* Architecture violations
* Rule violations
* Pattern violations
* Business logic violations
* EF Core issues
* LINQ inefficiencies
* Report calculation bugs
* Missing validation
* Missing tests

---

## When Creating New Code

Before generating code:

1. Read all referenced files.
2. Identify existing patterns.
3. Follow existing conventions.
4. Reuse existing abstractions.
5. Avoid introducing new patterns unless absolutely necessary.

---

## When Refactoring

Goals:

* Reduce complexity
* Remove duplication
* Improve readability
* Improve maintainability
* Preserve behavior
* Preserve architecture

Do NOT:

* Over-engineer
* Introduce unnecessary abstractions
* Rewrite working code without justification

---

## When Writing Tests

Create:

* Happy path tests
* Edge case tests
* Failure tests
* Regression tests

Focus especially on:

* Report calculations
* Revenue totals
* Delinquency calculations
* Collection summaries
* Payment workflows

---

## Final Instruction

Assume these files collectively define the architecture, business rules, and coding standards of the system.

Read them before making decisions.

If uncertain, consult the referenced files instead of making assumptions.
