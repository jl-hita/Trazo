export const ACCOUNT_COPY = {
  resetPassword: {
    savedTitle: 'Contraseña guardada',
    activatedMessage: 'Tu cuenta ya está activada. Puedes iniciar sesión con tu correo y la nueva contraseña.',
    signIn: 'Iniciar sesión',
    invalidInvitationTitle: 'Enlace de invitación no válido',
    missingToken: 'Falta el token de seguridad en este enlace. Pide al administrador que envíe una nueva invitación.',
    backToTrazo: 'Volver a TRAZO',
    setPasswordTitle: 'Configura tu contraseña',
    choosePassword: 'Elige una contraseña segura para activar tu cuenta de TRAZO.',
    newPassword: 'Nueva contraseña',
    confirmPassword: 'Confirmar contraseña',
    passwordHint: 'Al menos 12 caracteres, con mayúsculas, minúsculas y un número.',
    activate: 'Activar cuenta',
    invalidExpired: 'Este enlace no es válido, ha caducado o ya se ha utilizado. Pide al administrador una nueva invitación.',
    saveFailed: 'No se ha podido guardar la contraseña. Inténtalo de nuevo.'
  },
  verifyEmail: {
    verifyingTitle: 'Verificando tu correo',
    waitingMessage: 'Espera mientras verificamos tu dirección de correo electrónico.',
    invalidTitle: 'Enlace de verificación no válido',
    missingToken: 'Falta el token de seguridad en el enlace. Solicita un nuevo correo de verificación.',
    verifiedTitle: 'Correo verificado',
    verifiedMessage: 'Tu dirección de correo se ha verificado. Ya puedes iniciar sesión.',
    signIn: 'Continuar al inicio de sesión',
    expiredTitle: 'Enlace no válido o caducado',
    expiredMessage: 'Este enlace de verificación no es válido, ha caducado o ya se ha utilizado. Solicita uno nuevo.'
  }
} as const;
