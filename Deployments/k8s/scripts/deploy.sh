#!/usr/bin/env bash
# =============================================================================
# scripts/deploy.sh
# Full deployment script for DbPoolInsight on Kubernetes
#
# Usage:
#   ./scripts/deploy.sh dev dbpoolinsight    # deploy dev overlay
#   ./scripts/deploy.sh prod dbpoolinsight   # deploy prod overlay
#   ./scripts/deploy.sh loadtest # run k6 load test job
# =============================================================================

set -euo pipefail

ENVIRONMENT="${1:-dev}"
NAMESPACE="${2:-dbpoolinsight}" 
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"



### Helper Functions 
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'; NC='\033[0m'
# All Elements "$@"   args  | Length/Count $#  args.Length
# $*	string.Join(" ", args)	One long string: "arg1 arg2 arg3"
# $@	args (the actual array)	A collection: ["arg1", "arg2", "arg3"]
log()  { echo -e "${BLUE}[INFO]${NC} $*"; }
ok()   { echo -e "${GREEN}[ OK ]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
die()  { echo -e "${RED}[FAIL]${NC} $*" >&2; exit 1; }




# Main Functions 
# ── Validate prerequisites ────────────────────────────────────────────────────
check_prerequisites(){

log "Checking prerequisites...."
# 1. Check for kubectl and helm
for cmd in kubectl helm; do
    command -v "$cmd" $>/dev/null || die "$cmd is not installed"
done 
# 2. Check if kubectl has kustomize capabilities
# (Standard in kubectl v1.14+)
kubectl kustomize --help &>/dev/null || die "Your kubectl version does not support kustomize"
# 3. Check cluster connectivity
kubectl cluster-info &>/dev/null || die "kubectl cannot reach the cluster"
ok " Prerequisites Ok "

}

# ── Deploy app via Kustomize ──────────────────────────────────────────────────

deploy_app(){

  local OVERLAY="$ROOT_DIR/k8s/overlays/$ENVIRONMENT"
  [ -d "$OVERLAY" ] || die "Overlay not found: $OVERLAY"

  log "Deploying $ENVIRONMENT overlay..."
  kubectl apply -k "$OVERLAY"
  ok "Kustomize overlay applied"

  log "Waiting for SQL Server to be ready..."
  kubectl rollout status deployment/sqlserverdb -n "$NAMESPACE" --timeout=3m

  log "Waiting for EFCore API to be ready..."
  kubectl rollout status deployment/efcore-api -n "$NAMESPACE" --timeout=2m

  ok "Deployment complete!"

}






main(){

if [ "$#" -lt 1 ]; then
    die "Usage: $0 <environment> (dev|prod|loadtest)"
fi
check_prerequisites

case "$ENVIRONMENT" in 

dev|prod) #case
# block to run in case dev or prod  envirnoment 
log "Running $ENVIRONMENT ... "

# LATER 
# FIRST RUNNING install_monitoring
# SECOND RUNNING load_dashboard

# THIRD DEPLOY APP FOR SPECIFIC ENVIRONMENT 
deploy_app
# PRINT ACCESS INFO 





;;  #break
loadtest)  #case
# block to run in case loadtest envirnoment 
log "Running $ENVIRONMENT ... "

;; #break 
*) # default case
# block to run in case  not in previous cases  
die "Unknown environment: $ENVIRONMENT. Use: dev | prod | loadtest"

;;
esac 
}




# Entry Point
# All Elements "$@"   args  | Length/Count $#  args.Length
# $*	string.Join(" ", args)	One long string: "arg1 arg2 arg3"
# $@	args (the actual array)	A collection: ["arg1", "arg2", "arg3"]
main "$@"

