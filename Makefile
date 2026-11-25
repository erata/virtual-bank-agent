# Developer commands. Every target runs a real implementation.
BRUNO_CLI := @usebruno/cli@4.3.0

.PHONY: up down reset logs seed seed-check build test-unit test-arch test-db test-db-compose test-claims test-api smoke test-all test-stack ui-install ui-dev test-ui test-ui-e2e

up:              ## Build and start PostgreSQL, Keycloak, the API (:8081) and the web UI (http://localhost:4200); waits for health
	docker compose up -d --build --wait

down:            ## Stop containers; keeps the database volume
	docker compose down

reset:           ## Stop containers and delete the database volume (fresh seed on next up)
	docker compose down -v

logs:
	docker compose logs -f

seed:            ## Validate canonical EF seed fixtures (seeded by db-migrate one-shot job)
	dotnet run --project tools/VirtualBank.SeedGenerator

seed-check:      ## Validate EF seed fixture and Keycloak consistency
	dotnet run --project tools/VirtualBank.SeedGenerator -- --check

build:
	dotnet build VirtualBank.slnx

test-unit:       ## All .NET tests: unit, executable matrix, architecture rules (no Docker)
	dotnet test VirtualBank.slnx

test-arch:       ## Architecture rules only (layer dependencies, conventions)
	dotnet test tests/architecture/VirtualBank.ArchitectureTests

test-db:         ## RLS suite after EF Code First migrations in the Compose database
	docker compose up -d --wait postgres db-migrate
	PGHOST=localhost PGPORT=$${POSTGRES_PORT:-5432} PGUSER=postgres PGPASSWORD=$${POSTGRES_SUPERUSER_PASSWORD:-postgres-dev-only} scripts/test/db-tests.sh

test-db-compose: ## RLS suite against the Compose database (after make up)
	PGHOST=localhost PGPORT=$${POSTGRES_PORT:-5432} PGUSER=postgres PGPASSWORD=$${POSTGRES_SUPERUSER_PASSWORD:-postgres-dev-only} scripts/test/db-tests.sh

test-claims:     ## Keycloak issues the claims the authorization core expects (after make up)
	scripts/test/keycloak-claims.sh

test-api:        ## Bruno API contract and security collection (after make up)
	cd tests/api/bruno && npx --yes $(BRUNO_CLI) run --env local

UI := src/Frontend/virtual-bank-ui

ui-install:      ## Install the web UI dependencies (npm ci)
	cd $(UI) && npm ci --no-audit --no-fund

ui-dev:          ## Angular dev server on http://localhost:4200 (proxies /api to the API of `make up`; stop the frontend container first)
	cd $(UI) && npm start

test-ui:         ## Web UI unit tests, layer rules and production build (no stack needed)
	cd $(UI) && npm test && npm run test:architecture && npx ng build

test-ui-e2e:     ## Browser tests through the real Keycloak login against the running stack (after make up)
	cd $(UI) && npx playwright install chromium && npm run e2e

smoke:           ## Quick check that the running stack is wired up (after make up)
	scripts/test/api-smoke.sh

test-all: seed-check test-unit test-ui  ## Run test-db separately with pgvector Compose               ## Everything that needs no running stack

test-stack: test-db-compose test-claims smoke test-api test-ui-e2e   ## Everything against the running stack

# M3: actual local multilingual embeddings and pgvector-backed document search.
.PHONY: ai-up ai-down ai-pull ai-index test-rag-db
ai-up:
	docker compose --profile ai up -d ollama

ai-down:
	docker compose --profile ai stop ollama

ai-pull:
	@CHAT=$$(awk -F= '/^ASSISTANT_MODEL=/{print $$2; exit}' .env 2>/dev/null); \
	 EMBED=$$(awk -F= '/^DOCUMENTS_EMBEDDING_MODEL=/{print $$2; exit}' .env 2>/dev/null); \
	 docker compose --profile ai exec -T ollama ollama pull "$${CHAT:-qwen3:4b-instruct}" && \
	 docker compose --profile ai exec -T ollama ollama pull "$${EMBED:-bge-m3}"

ai-index:
	scripts/ai-index.sh

test-rag-db:
	PGHOST=localhost PGPORT=$${POSTGRES_PORT:-5432} PGUSER=postgres PGPASSWORD=$${POSTGRES_SUPERUSER_PASSWORD:-postgres-dev-only} scripts/test/document-rls-tests.sh
