#!/bin/bash
cd /tmp/kavia/workspace/code-generation/tic-tac-toe-classic-193282-193296/tic_tac_toe_backend
dotnet build --no-restore -v quiet -nologo -consoleloggerparameters:NoSummary /p:TreatWarningsAsErrors=false
LINT_EXIT_CODE=$?
if [ $LINT_EXIT_CODE -ne 0 ]; then
  exit 1
fi

