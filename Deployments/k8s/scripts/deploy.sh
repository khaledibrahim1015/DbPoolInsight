#!/usr/bin/env bash
# =============================================================================
# scripts/deploy.sh
# Full deployment script for DbPoolInsight on Kubernetes
#
# Usage:
#   ./scripts/deploy.sh dev dbpoolinsight    # deploy dev overlay
#   ./scripts/deploy.sh prod dbpoolinsight   # deploy prod overlay
#   ./scripts/deploy.sh loadtest             # run k6 load test job
# =============================================================================

# ── Bash Strict Mode ──────────────────────────────────────────────────────────
# set -e: Exit immediately if any command returns a non-zero (error) status.
# set -u: Exit if an undefined variable is referenced. Prevents silent bugs.
# set -o pipefail: Ensures that if any command in a pipeline fails (e.g., cmd1 | cmd2), 
#                  the entire pipeline fails, rather than just returning the status of cmd2.
set -euo pipefail

# ── Global Variables ──────────────────────────────────────────────────────────
# Use parameter expansion syntax ${VAR:-default} to set default values if none are provided.
ENVIRONMENT="${1:-dev}"          # Defaults to 'dev' if the first argument ($1) is missing
NAMESPACE="${2:-dbpoolinsight}"  # Defaults to 'dbpoolinsight' if the second argument ($2) is missing

# Resolve absolute paths safely, ensuring the script can be run from any directory.
# BASH_SOURCE[0] gets the script's path. dirname gets the directory. cd/pwd resolves it to an absolute path.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")" # Assumes root is one level up from the scripts folder


# ── Helper Functions & UI ─────────────────────────────────────────────────────
# ANSI color codes for terminal output formatting
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color (resets terminal color)

# Note on Bash array parameters:
# $#  -> Length/Count of arguments passed.
# $* -> Treats all arguments as a single concatenated string: "arg1 arg2 arg3"
# $@  -> Treats arguments as an array/collection: ["arg1", "arg2", "arg3"]. Generally preferred.

# Standardized logging functions
# 'echo -e' enables interpretation of backslash escapes (like colors).
# '>&2' redirects the 'die' output to standard error (stderr) instead of stdout.
log()  { echo -e "${BLUE}[INFO]${NC} $*"; }
ok()   { echo -e "${GREEN}[ OK ]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
die()  { echo -e "${RED}[FAIL]${NC} $*" >&2; exit 1; }


# ── Core Functions ────────────────────────────────────────────────────────────

# check_prerequisites
# Validates that the host machine has the necessary tools installed and configured
# before attempting to run deployment tasks.
check_prerequisites(){

  log "Checking prerequisites...."
  
  # 1. Check for kubectl and helm
  # 'command -v' is safer and more POSIX-compliant than 'which'.
  # '$>/dev/null' silences the output. If the command fails, 'die' is executed.
  for cmd in kubectl helm; do
    command -v "$cmd" &>/dev/null || die "$cmd is not installed"
  done 
  
  # 2. Check if kubectl has kustomize capabilities
  # Since kubectl v1.14, kustomize is built-in. We run a help check to verify.
  kubectl kustomize --help &>/dev/null || die "Your kubectl version does not support kustomize"
  
  # 3. Check cluster connectivity
  # Pings the cluster info endpoint. Fails if the user isn't logged into a cluster
  # or if the current kubeconfig context is invalid.
  kubectl cluster-info &>/dev/null || die "kubectl cannot reach the cluster"
  
  ok " Prerequisites Ok "
}

# deploy_app
# Handles the actual deployment to Kubernetes using Kustomize overlays.
deploy_app(){
  
  # Define the overlay path dynamically based on the ENVIRONMENT variable.
  local OVERLAY="$ROOT_DIR/k8s/overlays/$ENVIRONMENT"
  
  # Check if the calculated overlay directory actually exists on disk.
  [ -d "$OVERLAY" ] || die "Overlay not found: $OVERLAY"

  log "Deploying $ENVIRONMENT overlay..."
  
  # 'kubectl apply -k' processes the kustomization.yaml in the target directory
  # and applies the resulting manifests to the cluster.
  kubectl apply -k "$OVERLAY"
  ok "Kustomize overlay applied"

  log "Waiting for SQL Server to be ready..."
  # Halts script execution until the sqlserverdb deployment is fully rolled out.
  # Times out and fails the script if it takes longer than 3 minutes.
  kubectl rollout status deployment/sqlserverdb -n "$NAMESPACE" --timeout=3m

  log "Waiting for EFCore API to be ready..."
  # Halts script execution until the efcore-api deployment is ready (2-minute timeout).
  kubectl rollout status deployment/efcore-api -n "$NAMESPACE" --timeout=2m

  ok "Deployment complete!"
}

# ── Print access info ─────────────────────────────────────────────────────────
print_access_info() {
  echo ""
  echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
  echo -e "${GREEN}  DbPoolInsight deployed to: $ENVIRONMENT${NC}"
  echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
  echo ""

# currently we implement for both dev and prod 
    echo "  Port-forward commands (run in separate terminals):"
    echo "    kubectl port-forward svc/efcore-api 8080:8080 -n $NAMESPACE"

    echo ""
    echo "  Then open:"
    echo "    API:        http://localhost:8080/swagger/index.html"
    echo "    OTEL:        http://localhost:8080/metrics"
    echo "    HEALTH:        http://localhost:8080/health"

  echo ""
}




# ── Main Execution Controller ─────────────────────────────────────────────────

main(){
  # Optional: You can enforce that at least one argument is provided by uncommenting the block below.
  # Since you mapped defaults for $1 and $2 above, this check might conflict if you want
  # to allow parameterless execution (defaulting to dev). 
  #
  # if [ "$#" -lt 1 ]; then
  #   die "Usage: $0 <environment> (dev|prod|loadtest)"
  # fi
  
  check_prerequisites

  # Route the script's behavior based on the specified environment
  case "$ENVIRONMENT" in 
    dev|prod)
      # Execution block for DEV or PROD environments
      log "Running $ENVIRONMENT ... "

      # [TODO]: Future additions can be placed here.
      # LATER 
      # FIRST RUNNING install_monitoring
      # SECOND RUNNING load_dashboard

      # THIRD DEPLOY APP FOR SPECIFIC ENVIRONMENT 
      deploy_app
      
      # [TODO]: PRINT ACCESS INFO (e.g., retrieving NodePort, LoadBalancer IP, or Ingress host)
      print_access_info
      ;; # Break out of case statement

    loadtest)
      # Execution block for LOADTEST environment
      log "Running $ENVIRONMENT ... "
      # [TODO]: Add k6 execution or loadtest Job creation commands here.
      ;; # Break out of case statement
      
    *) 
      # Default catch-all block for unhandled or invalid environment arguments
      die "Unknown environment: $ENVIRONMENT. Use: dev | prod | loadtest"
      ;;
  esac 
}

# ── Entry Point ───────────────────────────────────────────────────────────────
# Passes all script arguments directly into the main function.
# Using "$@" ensures that arguments with spaces are preserved correctly.
main "$@"